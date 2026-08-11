// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Sign.Core.Test
{
    public class StaticWebAssetFilterTests
    {
        private static readonly string PackageRootDirectoryPath = Path.Combine(Path.GetTempPath(), "package");

        private readonly StaticWebAssetFilter _filter;

        public StaticWebAssetFilterTests()
        {
            _filter = new StaticWebAssetFilter(Substitute.For<ILogger<IStaticWebAssetFilter>>());
        }

        [Fact]
        public void Constructor_WhenLoggerIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new StaticWebAssetFilter(logger: null!));

            Assert.Equal("logger", exception.ParamName);
        }

        [Fact]
        public void Filter_WhenFilesIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => _filter.Filter(files: null!));

            Assert.Equal("files", exception.ParamName);
        }

        [Fact]
        public void Filter_WhenFilesIsEmpty_ReturnsEmpty()
        {
            IReadOnlyList<FileInfo> files = _filter.Filter(Enumerable.Empty<FileInfo>());

            Assert.Empty(files);
        }

        [Fact]
        public void Filter_WhenPropsFileIsMissing_ReturnsAllFiles()
        {
            // Without the marker file, a .js file is indistinguishable from a Windows Script Host script.
            AssertFilter(
                files: new[] { "staticwebassets/app.js", "lib/net8.0/a.dll" },
                expectedFilesToSign: new[] { "staticwebassets/app.js", "lib/net8.0/a.dll" });
        }

        [Theory]
        [InlineData("build")]
        [InlineData("buildMultiTargeting")]
        [InlineData("buildTransitive")]
        [InlineData("BUILD")] // test case insensitivity
        public void Filter_WhenPropsFileIsInBuildDirectory_ExcludesStaticWebAssets(string buildDirectoryName)
        {
            AssertFilter(
                files: new[]
                {
                    $"{buildDirectoryName}/Microsoft.AspNetCore.StaticWebAssets.props",
                    "staticwebassets/app.js",
                    "lib/net8.0/a.dll"
                },
                expectedFilesToSign: new[]
                {
                    $"{buildDirectoryName}/Microsoft.AspNetCore.StaticWebAssets.props",
                    "lib/net8.0/a.dll"
                });
        }

        [Theory]
        [InlineData("Microsoft.AspNetCore.StaticWebAssets.props")]
        [InlineData("Microsoft.AspNetCore.StaticWebAssetEndpoints.props")] // .NET 9 and later
        [InlineData("microsoft.aspnetcore.staticwebassets.props")] // test case insensitivity
        public void Filter_WhenPropsFileNameIsRecognized_ExcludesStaticWebAssets(string propsFileName)
        {
            AssertFilter(
                files: new[] { $"build/{propsFileName}", "staticwebassets/app.js" },
                expectedFilesToSign: new[] { $"build/{propsFileName}" });
        }

        [Fact]
        public void Filter_WhenPropsFileIsNotInBuildDirectory_ReturnsAllFiles()
        {
            AssertFilter(
                files: new[] { "Microsoft.AspNetCore.StaticWebAssets.props", "staticwebassets/app.js" },
                expectedFilesToSign: new[] { "Microsoft.AspNetCore.StaticWebAssets.props", "staticwebassets/app.js" });
        }

        [Fact]
        public void Filter_WhenPropsFileNameIsUnrelated_ReturnsAllFiles()
        {
            AssertFilter(
                files: new[] { "build/MyPackage.props", "staticwebassets/app.js" },
                expectedFilesToSign: new[] { "build/MyPackage.props", "staticwebassets/app.js" });
        }

        [Theory]
        [InlineData("staticwebassets/app.js")]
        [InlineData("staticwebassets/_framework/a.dll")]
        [InlineData("staticwebassets/_framework/dotnet.native.wasm")]
        [InlineData("staticwebassets/js/nested/directory/app.js")]
        [InlineData("STATICWEBASSETS/app.js")] // test case insensitivity
        public void Filter_WhenFileIsStaticWebAsset_ExcludesFile(string filePath)
        {
            AssertFilter(
                files: new[] { "build/Microsoft.AspNetCore.StaticWebAssets.props", filePath },
                expectedFilesToSign: new[] { "build/Microsoft.AspNetCore.StaticWebAssets.props" });
        }

        [Theory]
        [InlineData("tools/script.js")] // a Windows Script Host script
        [InlineData("contentFiles/any/any/script.js")]
        [InlineData("staticwebassetsdocs/app.js")] // similarly named directory
        public void Filter_WhenFileIsNotStaticWebAsset_ReturnsFile(string filePath)
        {
            AssertFilter(
                files: new[] { "build/Microsoft.AspNetCore.StaticWebAssets.props", filePath },
                expectedFilesToSign: new[] { "build/Microsoft.AspNetCore.StaticWebAssets.props", filePath });
        }

        [Fact]
        public void Filter_WhenStaticWebAssetsDirectoryIsNotUnderPackageRoot_ReturnsFile()
        {
            // The props file identifies its own package's static web assets, not another package's.
            AssertFilter(
                files: new[]
                {
                    "build/Microsoft.AspNetCore.StaticWebAssets.props",
                    "tools/staticwebassets/app.js"
                },
                expectedFilesToSign: new[]
                {
                    "build/Microsoft.AspNetCore.StaticWebAssets.props",
                    "tools/staticwebassets/app.js"
                });
        }

        [Fact]
        public void Filter_WhenFileIsStaticWebAsset_LogsWarning()
        {
            Logger logger = new();
            StaticWebAssetFilter filter = new(logger);

            filter.Filter(GetFiles("build/Microsoft.AspNetCore.StaticWebAssets.props", "staticwebassets/app.js"));

            Assert.Equal(1, logger.Log_CallCount);
        }

        [Fact]
        public void Filter_WhenNoFileIsStaticWebAsset_LogsNothing()
        {
            Logger logger = new();
            StaticWebAssetFilter filter = new(logger);

            filter.Filter(GetFiles("lib/net8.0/a.dll", "tools/script.js"));

            Assert.Equal(0, logger.Log_CallCount);
        }

        private void AssertFilter(string[] files, string[] expectedFilesToSign)
        {
            IReadOnlyList<FileInfo> actualFilesToSign = _filter.Filter(GetFiles(files));

            Assert.Equal(
                GetFiles(expectedFilesToSign).Select(file => file.FullName),
                actualFilesToSign.Select(file => file.FullName));
        }

        private static IReadOnlyList<FileInfo> GetFiles(params string[] relativeFilePaths)
        {
            return relativeFilePaths
                .Select(relativeFilePath => new FileInfo(
                    Path.Combine(
                        PackageRootDirectoryPath,
                        relativeFilePath.Replace('/', Path.DirectorySeparatorChar))))
                .ToList();
        }

        private sealed class Logger : ILogger<IStaticWebAssetFilter>
        {
            internal int Log_CallCount { get; private set; }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                return new NoOpDisposable();
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                ++Log_CallCount;

                Assert.Equal(LogLevel.Warning, logLevel);
            }

            private sealed class NoOpDisposable : IDisposable
            {
                public void Dispose()
                {
                }
            }
        }
    }
}

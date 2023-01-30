// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Sign.Core.Test
{
    public class ServiceProviderTests
    {
        [Fact]
        public void Constructor_WhenServiceProviderIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ServiceProvider(serviceProvider: null!));

            Assert.Equal("serviceProvider", exception.ParamName);
        }

        [Fact]
        public void CreateDefault_Always_RegistersRequiredServices()
        {
            ServiceProvider serviceProvider = ServiceProvider.CreateDefault();

            // Start of tests
            Assert.NotNull(serviceProvider.GetRequiredService<ILogger<ServiceProviderTests>>());
            Assert.NotNull(serviceProvider.GetRequiredService<IAppRootDirectoryLocator>());
            Assert.NotNull(serviceProvider.GetRequiredService<IToolConfigurationProvider>());
            Assert.NotNull(serviceProvider.GetRequiredService<IMatcherFactory>());
            Assert.NotNull(serviceProvider.GetRequiredService<IFileListReader>());
            Assert.NotNull(serviceProvider.GetRequiredService<IFileMatcher>());
            Assert.NotNull(serviceProvider.GetRequiredService<IContainerProvider>());
            Assert.NotNull(serviceProvider.GetRequiredService<IFileMetadataService>());
            Assert.NotNull(serviceProvider.GetRequiredService<IDirectoryService>());
            Assert.NotNull(serviceProvider.GetRequiredService<IKeyVaultService>());

            IDefaultSignatureProvider defaultSignatureProvider = serviceProvider.GetRequiredService<IDefaultSignatureProvider>();
            Assert.IsType<AzureSignToolSignatureProvider>(defaultSignatureProvider.SignatureProvider);

            IEnumerable<ISignatureProvider> signatureProviders = serviceProvider.GetServices<ISignatureProvider>();
            Assert.Equal(5, signatureProviders.Count());

            Assert.NotNull(serviceProvider.GetRequiredService<IAggregatingSignatureProvider>());

            Assert.NotNull(serviceProvider.GetRequiredService<IManifestSigner>());
            Assert.NotNull(serviceProvider.GetRequiredService<IMageCli>());
            Assert.NotNull(serviceProvider.GetRequiredService<IMakeAppxCli>());
            Assert.NotNull(serviceProvider.GetRequiredService<INuGetSignTool>());
            Assert.NotNull(serviceProvider.GetRequiredService<IOpenVsixSignTool>());
            Assert.NotNull(serviceProvider.GetRequiredService<ICertificateVerifier>());
            Assert.NotNull(serviceProvider.GetRequiredService<ISigner>());
        }

        [Theory]
        [InlineData(LogLevel.Trace)]
        [InlineData(LogLevel.Debug)]
        [InlineData(LogLevel.Information)]
        [InlineData(LogLevel.Warning)]
        [InlineData(LogLevel.Error)]
        [InlineData(LogLevel.Critical)]
        [InlineData(LogLevel.None)]
        public void CreateDefault_Always_ConfiguresLoggingVerbosity(LogLevel logLevel)
        {
            TestLoggerProvider loggerProvider = new();
            ServiceProvider serviceProvider = ServiceProvider.CreateDefault(logLevel, loggerProvider);

            ILogger logger = serviceProvider.GetRequiredService<ILogger<ServiceProviderTests>>();

            logger.LogTrace("trace");
            logger.LogDebug("debug");
            logger.LogInformation("information");
            logger.LogWarning("warning");
            logger.LogError("error");
            logger.LogCritical("error");

            LoggerSpy loggerSpy = Assert.Single(loggerProvider.Loggers);

            Assert.Equal(LogLevel.None - logLevel, loggerSpy.Logs.Count);

            Assert.Equal(logLevel <= LogLevel.Trace ? 1 : 0, loggerSpy.Logs.Count(pair => pair.Key == LogLevel.Trace));
            Assert.Equal(logLevel <= LogLevel.Debug ? 1 : 0, loggerSpy.Logs.Count(pair => pair.Key == LogLevel.Debug));
            Assert.Equal(logLevel <= LogLevel.Information ? 1 : 0, loggerSpy.Logs.Count(pair => pair.Key == LogLevel.Information));
            Assert.Equal(logLevel <= LogLevel.Warning ? 1 : 0, loggerSpy.Logs.Count(pair => pair.Key == LogLevel.Warning));
            Assert.Equal(logLevel <= LogLevel.Error ? 1 : 0, loggerSpy.Logs.Count(pair => pair.Key == LogLevel.Error));
            Assert.Equal(logLevel <= LogLevel.Critical ? 1 : 0, loggerSpy.Logs.Count(pair => pair.Key == LogLevel.Critical));
        }

        private sealed class TestLoggerProvider : ILoggerProvider
        {
            private readonly List<LoggerSpy> _loggers = new();

            internal IReadOnlyList<LoggerSpy> Loggers => _loggers;

            public ILogger CreateLogger(string categoryName)
            {
                LoggerSpy logger = new(categoryName);

                _loggers.Add(logger);

                return logger;
            }

            public void Dispose()
            {
            }
        }

        private sealed class LoggerSpy : ILogger
        {
            private readonly Dictionary<LogLevel, int> _logs = new();

            internal IReadOnlyDictionary<LogLevel, int> Logs => _logs;

            internal string CategoryName { get; }

            internal LoggerSpy(string categoryName)
            {
                CategoryName = categoryName;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!Logs.TryGetValue(logLevel, out int count))
                {
                    count = 0;
                }

                _logs[logLevel] = ++count;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                throw new NotImplementedException();
            }
        }
    }
}
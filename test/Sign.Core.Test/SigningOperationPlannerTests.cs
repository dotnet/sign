// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core.Test
{
    public sealed class SigningOperationPlannerTests
    {
        [Fact]
        public void Create_SingleInputAndOutputFile_UsesOutputFile()
        {
            using TestDirectory directory = new();
            FileInfo input = new(
                Path.Combine(directory.FullPath, "input.dll"));

            SigningOperationPlan plan = Assert.Single(
                SigningOperationPlanner.Create(
                    new[] { input },
                    "signed.dll",
                    new DirectoryInfo(directory.FullPath)));

            Assert.Equal(
                Path.Combine(directory.FullPath, "signed.dll"),
                plan.Output.FullName);
            Assert.Equal(
                SigningSourceIdentity.Capture(input),
                plan.Source.SourceIdentity);
        }

        [Fact]
        public void Create_SingleInputAndOutputDirectory_AppendsInputName()
        {
            using TestDirectory directory = new();
            FileInfo input = new(
                Path.Combine(directory.FullPath, "input.dll"));

            SigningOperationPlan plan = Assert.Single(
                SigningOperationPlanner.Create(
                    new[] { input },
                    "signed",
                    new DirectoryInfo(directory.FullPath)));

            Assert.Equal(
                Path.Combine(
                    directory.FullPath,
                    "signed",
                    input.Name),
                plan.Output.FullName);
        }

        [Fact]
        public void Create_MultipleInputs_PreservesRelativePaths()
        {
            using TestDirectory directory = new();
            FileInfo first = new(
                Path.Combine(directory.FullPath, "first.dll"));
            FileInfo second = new(
                Path.Combine(
                    directory.FullPath,
                    "nested",
                    "second.dll"));

            IReadOnlyList<SigningOperationPlan> plans =
                SigningOperationPlanner.Create(
                    new[] { first, second },
                    "signed",
                    new DirectoryInfo(directory.FullPath));

            Assert.Collection(
                plans,
                plan => Assert.Equal(
                    Path.Combine(
                        directory.FullPath,
                        "signed",
                        "first.dll"),
                    plan.Output.FullName),
                plan => Assert.Equal(
                    Path.Combine(
                        directory.FullPath,
                        "signed",
                        "nested",
                        "second.dll"),
                    plan.Output.FullName));
        }

        [Fact]
        public void Create_WhenInputFilesContainsNull_Throws()
        {
            using TestDirectory directory = new();

            ArgumentException exception =
                Assert.Throws<ArgumentException>(
                    () => SigningOperationPlanner.Create(
                        new FileInfo[] { null! },
                        output: null,
                        new DirectoryInfo(directory.FullPath)));

            Assert.Equal("inputFiles", exception.ParamName);
            Assert.StartsWith(
                "The collection cannot contain a null element.",
                exception.Message);
        }

        [Fact]
        public void Create_MultipleInputsOutsideBaseDirectory_MapsRelativeToOutputDirectory()
        {
            using TestDirectory baseDirectory = new();
            using TestDirectory outsideDirectory = new();
            FileInfo inside = new(
                Path.Combine(baseDirectory.FullPath, "inside.dll"));
            FileInfo outside = new(
                Path.Combine(outsideDirectory.FullPath, "outside.dll"));
            string outputDirectory = Path.Combine(
                baseDirectory.FullPath,
                "signed");

            IReadOnlyList<SigningOperationPlan> plans =
                SigningOperationPlanner.Create(
                    new[] { inside, outside },
                    "signed",
                    new DirectoryInfo(baseDirectory.FullPath));

            // Matches upstream, which maps the output outside the output
            // directory.
            Assert.Equal(
                Path.GetFullPath(
                    Path.Combine(
                        outputDirectory,
                        Path.GetRelativePath(
                            baseDirectory.FullPath,
                            outside.FullName))),
                plans[1].Output.FullName);
        }

        [Fact]
        public void Create_MultipleInputsOnDifferentDrive_MapsToSource()
        {
            using TestDirectory directory = new();
            string baseRoot = Path.GetPathRoot(directory.FullPath)!;
            char otherDrive = char.ToUpperInvariant(baseRoot[0]) == 'C'
                ? 'D'
                : 'C';
            FileInfo inside = new(
                Path.Combine(directory.FullPath, "inside.dll"));
            FileInfo crossDrive = new(
                $@"{otherDrive}:\outside.dll");

            IReadOnlyList<SigningOperationPlan> plans =
                SigningOperationPlanner.Create(
                    new[] { inside, crossDrive },
                    "signed",
                    new DirectoryInfo(directory.FullPath));

            // Matches upstream: the relative path is rooted, so it replaces
            // the output directory.
            Assert.Equal(
                crossDrive.FullName,
                plans[1].Output.FullName);
        }

        [Fact]
        public void Create_MultipleInputsUnderBaseDirectory_StayUnderOutputRoot()
        {
            using TestDirectory directory = new();
            DirectoryInfo baseDirectory =
                new(directory.FullPath);
            string output = Path.Combine(
                directory.FullPath,
                "signed");
            FileInfo first = new(
                Path.Combine(directory.FullPath, "first.dll"));
            FileInfo second = new(
                Path.Combine(
                    directory.FullPath,
                    "nested",
                    "second.dll"));

            IReadOnlyList<SigningOperationPlan> plans =
                SigningOperationPlanner.Create(
                    new[] { first, second },
                    output,
                    baseDirectory);
            string outputRoot =
                $"{Path.GetFullPath(output)}{Path.DirectorySeparatorChar}";

            Assert.All(
                plans,
                plan => Assert.StartsWith(
                    outputRoot,
                    plan.Output.FullName,
                    StringComparison.OrdinalIgnoreCase));
        }
    }
}

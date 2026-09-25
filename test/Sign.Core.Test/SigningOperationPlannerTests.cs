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
    }
}

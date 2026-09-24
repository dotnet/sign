// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal static class SigningOperationPlanner
    {
        internal static IReadOnlyList<SigningOperationPlan> Create(
            IReadOnlyList<FileInfo> inputFiles,
            string? output,
            DirectoryInfo baseDirectory)
        {
            ArgumentNullException.ThrowIfNull(
                inputFiles,
                nameof(inputFiles));
            ArgumentNullException.ThrowIfNull(
                baseDirectory,
                nameof(baseDirectory));

            List<SigningOperationPlan> plans =
                new(inputFiles.Count);

            foreach (FileInfo input in inputFiles)
            {
                if (input is null)
                {
                    throw new ArgumentException(
                        message: "The collection cannot contain a null element.",
                        paramName: nameof(inputFiles));
                }

                plans.Add(
                    new SigningOperationPlan(
                        SigningFile.Capture(input),
                        GetOutput(
                            input,
                            inputFiles.Count,
                            output,
                            baseDirectory)));
            }

            return plans;
        }

        private static FileInfo GetOutput(
            FileInfo input,
            int inputCount,
            string? output,
            DirectoryInfo baseDirectory)
        {
            if (inputCount == 1 &&
                !string.IsNullOrWhiteSpace(output))
            {
                string outputPath = ExpandPath(
                    baseDirectory,
                    output);

                return Path.HasExtension(output)
                    ? new FileInfo(outputPath)
                    : new FileInfo(
                        Path.Combine(outputPath, input.Name));
            }

            if (string.IsNullOrWhiteSpace(output))
            {
                return new FileInfo(input.FullName);
            }

            string relativePath = Path.GetRelativePath(
                baseDirectory.FullName,
                input.FullName);
            string outputDirectory = ExpandPath(
                baseDirectory,
                output);

            return new FileInfo(
                Path.Combine(outputDirectory, relativePath));
        }

        private static string ExpandPath(
            DirectoryInfo baseDirectory,
            string path)
        {
            return Path.IsPathRooted(path)
                ? path
                : Path.Combine(baseDirectory.FullName, path);
        }
    }
}

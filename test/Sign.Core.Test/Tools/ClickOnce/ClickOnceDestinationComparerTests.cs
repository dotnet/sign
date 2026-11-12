// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Sign.TestInfrastructure;

namespace Sign.Core.Test
{
    public sealed class ClickOnceDestinationComparerTests
    {
        [Fact]
        public void MissingDestinations_InInsensitiveDirectory_AreEquivalent()
        {
            using TestDirectory directory = new();
            string first = Path.Combine(directory.FullPath, "Output", "payload.dll");
            string second = Path.Combine(directory.FullPath, "OUTPUT", "PAYLOAD.dll");

            Assert.True(ClickOnceDestinationComparer.Instance.Equals(first, second));
            Assert.Equal(ClickOnceDestinationComparer.Instance.GetHashCode(first),
                ClickOnceDestinationComparer.Instance.GetHashCode(second));
            Assert.False(Directory.Exists(Path.Combine(directory.FullPath, "Output")));
        }

        [RequiresCaseSensitiveDirectoryFact]
        public void MissingDestinations_InSensitiveDirectory_AreDistinct()
        {
            using TestDirectory directory = new();
            Assert.True(CaseSensitiveDirectory.TryEnable(new DirectoryInfo(directory.FullPath)));
            string first = Path.Combine(directory.FullPath, "Output", "payload.dll");
            string second = Path.Combine(directory.FullPath, "output", "payload.dll");

            Assert.False(ClickOnceDestinationComparer.Instance.Equals(first, second));
            Assert.False(Directory.Exists(Path.Combine(directory.FullPath, "Output")));
        }

        [RequiresCaseSensitiveDirectoryFact]
        public void Equals_WhenExistingParentDirectoriesDifferByCaseInSensitiveRoot_KeepsDestinationsDistinct()
        {
            using TestDirectory directory = new();
            DirectoryInfo root = new(directory.FullPath);
            Assert.True(CaseSensitiveDirectory.TryEnable(root));
            DirectoryInfo upper = root.CreateSubdirectory("Output");
            DirectoryInfo lower = root.CreateSubdirectory("output");
            string first = Path.Combine(upper.FullName, "payload.dll");
            string second = Path.Combine(lower.FullName, "payload.dll");

            Assert.False(ClickOnceDestinationComparer.Instance.Equals(first, second));
        }
    }
}

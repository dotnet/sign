// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core.Test
{
    public class FileInfoComparerTests
    {
        private readonly FileInfoComparer _instance = FileInfoComparer.Instance;

        [Fact]
        public void Instance_Always_ReturnsSameInstance()
        {
            FileInfoComparer instance0 = FileInfoComparer.Instance;
            FileInfoComparer instance1 = FileInfoComparer.Instance;

            Assert.Same(instance0, instance1);
        }

        [Fact]
        public void Equals_WhenArgumentsAreSameInstance_ReturnsTrue()
        {
            using (TemporaryFile temporaryFile = new())
            {
                Assert.True(_instance.Equals(temporaryFile.File, temporaryFile.File));
            }
        }

        [Fact]
        public void Equals_WhenArgumentsAreDifferentInstancesWithSameFullName_ReturnsTrue()
        {
            using (TemporaryFile temporaryFile = new())
            {
                FileInfo otherFile = new(temporaryFile.File.FullName);

                Assert.True(_instance.Equals(temporaryFile.File, otherFile));
            }
        }

        [Fact]
        public void Equals_WhenArgumentsAreDifferentInstancesWithSameFullNameButDifferentCasing_ReturnsFalse()
        {
            using (TemporaryFile temporaryFile = new())
            {
                FileInfo oneFile = new(temporaryFile.File.FullName.ToUpperInvariant());
                FileInfo anotherFile = new(temporaryFile.File.FullName.ToLowerInvariant());

                Assert.False(_instance.Equals(oneFile, anotherFile));
            }
        }

        [Fact]
        public void Equals_WhenArgumentsAreDifferentInstancesWithDifferentFullName_ReturnsFalse()
        {
            using (TemporaryFile temporaryFile0 = new())
            using (TemporaryFile temporaryFile1 = new())
            {
                Assert.False(_instance.Equals(temporaryFile0.File, temporaryFile1.File));
            }
        }

        [Fact]
        public void Equals_WhenOnlyOneArgumentIsNull_ReturnsFalse()
        {
            using (TemporaryFile temporaryFile = new())
            {
                Assert.False(_instance.Equals(null, temporaryFile.File));
                Assert.False(_instance.Equals(temporaryFile.File, null));
            }
        }

        [Fact]
        public void Equals_WhenBothArgumentsAreNull_ReturnsTrue()
        {
            Assert.True(_instance.Equals(null, null));
        }

        [Fact]
        public void GetHashCode_Always_ReturnsFullNameHashCode()
        {
            using (TemporaryFile temporaryFile = new())
            {
                Assert.Equal(
                    temporaryFile.File.FullName.GetHashCode(),
                    FileInfoComparer.Instance.GetHashCode(temporaryFile.File));
            }
        }
    }
}
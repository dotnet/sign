// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Moq;

namespace Sign.Core.Test
{
    public sealed class ClickOnceManifestReaderProviderTests
    {
        [Fact]
        public void Reader_WhenNotAccessed_DoesNotCreateManifestReader()
        {
            int invocationCount = 0;
            ClickOnceManifestReaderProvider provider = new(
                () =>
                {
                    ++invocationCount;

                    return Mock.Of<IClickOnceManifestReader>();
                });

            Assert.False(provider.IsValueCreated);
            Assert.Equal(0, invocationCount);
        }

        [Fact]
        public void Reader_WhenAccessedMultipleTimes_CreatesManifestReaderOnce()
        {
            int invocationCount = 0;
            IClickOnceManifestReader expectedReader = Mock.Of<IClickOnceManifestReader>();
            ClickOnceManifestReaderProvider provider = new(
                () =>
                {
                    ++invocationCount;

                    return expectedReader;
                });

            IClickOnceManifestReader firstReader = provider.Reader;
            IClickOnceManifestReader secondReader = provider.Reader;

            Assert.Same(expectedReader, firstReader);
            Assert.Same(firstReader, secondReader);
            Assert.True(provider.IsValueCreated);
            Assert.Equal(1, invocationCount);
        }
    }
}

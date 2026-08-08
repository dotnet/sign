// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal sealed class ClickOnceManifestReaderProvider
    {
        private readonly Lazy<IClickOnceManifestReader> _reader;

        internal ClickOnceManifestReaderProvider()
            : this(static () => new ClickOnceManifestReader())
        {
        }

        internal ClickOnceManifestReaderProvider(Func<IClickOnceManifestReader> readerFactory)
        {
            ArgumentNullException.ThrowIfNull(readerFactory, nameof(readerFactory));

            _reader = new Lazy<IClickOnceManifestReader>(
                readerFactory,
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        internal bool IsValueCreated => _reader.IsValueCreated;
        internal IClickOnceManifestReader Reader => _reader.Value;
    }
}

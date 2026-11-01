// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal sealed class SigningOperationStage : IDisposable
    {
        private readonly TemporaryDirectory _temporaryDirectory;

        internal SigningOperationStage(
            TemporaryDirectory temporaryDirectory,
            SigningFile source,
            SigningFile input)
        {
            ArgumentNullException.ThrowIfNull(
                temporaryDirectory,
                nameof(temporaryDirectory));
            ArgumentNullException.ThrowIfNull(source, nameof(source));
            ArgumentNullException.ThrowIfNull(input, nameof(input));

            _temporaryDirectory = temporaryDirectory;
            Source = source;
            Input = input;
        }

        internal SigningFile Source { get; }
        internal SigningFile Input { get; }

        public void Dispose()
        {
            _temporaryDirectory.Dispose();
        }
    }
}

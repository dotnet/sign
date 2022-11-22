// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core.Test
{
    internal sealed class AggregatingSignatureProviderSpy : IAggregatingSignatureProvider
    {
        internal List<FileInfo> FilesSubmittedForSigning { get; } = new();

        public bool CanSign(FileInfo file)
        {
            throw new NotImplementedException();
        }

        public Task SignAsync(IEnumerable<FileInfo> files, SignOptions options)
        {
            FilesSubmittedForSigning.AddRange(files);

            return Task.CompletedTask;
        }
    }
}
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.Logging;
using NuGet.Packaging.Signing;

namespace Sign.Core
{
    internal sealed class NuGetContainer : ZipContainer
    {
        internal NuGetContainer(
            FileInfo zipFile,
            IDirectoryService directoryService,
            IFileMatcher fileMatcher, ILogger logger)
            : base(zipFile, directoryService, fileMatcher, logger)
        {
        }

        public override ValueTask SaveAsync()
        {
            if (TemporaryDirectory is null)
            {
                throw new InvalidOperationException();
            }

            FileInfo signatureFile = new(
                Path.Combine(
                    TemporaryDirectory.Directory.FullName,
                    SigningSpecifications.V1.SignaturePath));

            if (signatureFile.Exists)
            {
                signatureFile.Delete();
            }

            return base.SaveAsync();
        }
    }
}
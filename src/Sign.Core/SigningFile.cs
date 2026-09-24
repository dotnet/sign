// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal sealed class SigningFile
    {
        internal SigningFile(
            FileInfo file,
            SigningSourceIdentity sourceIdentity)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));
            ArgumentNullException.ThrowIfNull(
                sourceIdentity,
                nameof(sourceIdentity));

            File = file;
            SourceIdentity = sourceIdentity;
        }

        internal FileInfo File { get; }
        internal SigningSourceIdentity SourceIdentity { get; }

        internal static SigningFile Capture(FileInfo file)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));

            return new SigningFile(
                file,
                SigningSourceIdentity.Capture(file));
        }

        internal static SigningFile ContainerEntry(
            FileInfo file,
            SigningSourceIdentity parentIdentity,
            string entryPath)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));
            ArgumentNullException.ThrowIfNull(
                parentIdentity,
                nameof(parentIdentity));

            return new SigningFile(
                file,
                SigningSourceIdentity.ContainerEntry(
                    parentIdentity,
                    entryPath));
        }
    }
}

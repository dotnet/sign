// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal interface ISigningDependencyProvider
    {
        /// <summary>
        /// Gets the files that <paramref name="file"/> owns.
        /// </summary>
        IReadOnlyList<FileInfo> GetSigningDependencies(FileInfo file);

        /// <summary>
        /// Removes the files owned by another file in <paramref name="files"/>.  Owned files are
        /// signed as part of their owner instead.
        /// </summary>
        IReadOnlyList<FileInfo> ExcludeOwnedFiles(IEnumerable<FileInfo> files);
    }
}

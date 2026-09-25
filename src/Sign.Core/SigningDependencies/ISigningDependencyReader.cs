// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    /// <summary>
    /// Reads the signing dependencies of one file format.
    /// </summary>
    internal interface ISigningDependencyReader
    {
        bool CanRead(FileInfo file);

        /// <summary>
        /// Gets the files that <paramref name="file"/> owns.  Owned files must not be signed
        /// independently of their owner; doing so leaves the owner referring to content that no
        /// longer exists.
        /// </summary>
        IReadOnlyList<FileInfo> GetSigningDependencies(FileInfo file);
    }
}

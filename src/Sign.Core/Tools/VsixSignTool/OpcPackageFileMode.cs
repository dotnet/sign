// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    /// <summary>
    /// Sets the mode of the package when opened.
    /// </summary>
    internal enum OpcPackageFileMode
    {
        /// <summary>
        /// The package will be opened in read-only mode.
        /// </summary>
        Read,

        /// <summary>
        /// The package will be opened for reading and writing.
        /// </summary>
        ReadWrite
    }
}

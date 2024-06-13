// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal interface IDataFormatSigner
    {
        bool CanSign(FileInfo file);
        Task SignAsync(IEnumerable<FileInfo> files, SignOptions options);
        // Some signature mechanisms (e.g. ClickOnce) require extra files alongside the main file to be signed.
        // We can't rely on the user specifying everything (and even if we did, we sign all inputs in parallel
        // so we'd have to add extra synchronisation) so this method instructs an implementation to grab all
        // dependencies of a file and copy them to the specified directory.
        void CopySigningDependencies(FileInfo file, DirectoryInfo destination, SignOptions options) { }
    }
}
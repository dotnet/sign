// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal interface IAggregatingDataFormatSigner : IDataFormatSigner
    {
        // Whether signing would process the file, either with a signer or, when recursion is enabled, as a
        // container. Files without signing work must not claim a coordinated signing operation; its
        // unchanged result would otherwise be reused by a caller that must sign the same file, such as
        // ClickOnce signing an application manifest.
        bool HasSigningWork(FileInfo file, SignOptions options);

        // Whether any signer for the file requires sequential coordination.
        bool IsSequentialCoordinationRequired(FileInfo file);

        // Whether any signer for the file requires the staged file to keep its original file name.
        bool IsOriginalFileNameRequired(FileInfo file);

        Task SignAsync(
            IEnumerable<SigningFile> files,
            SignOptions options,
            SigningOperationCoordinator coordinator);

        Task SignOwnerAsync(
            SigningFile file,
            SignOptions options,
            SigningOperationCoordinator coordinator);
    }
}
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal sealed class ClickOnceApplicationSigningContext
    {
        private readonly HashSet<SigningSourceIdentity> _explicitPayloads;

        // Orchestration registers independently selected operations before
        // starting applications that consume them. This context never claims work.
        internal ClickOnceApplicationSigningContext(IEnumerable<SigningSourceIdentity> explicitPayloads)
        {
            ArgumentNullException.ThrowIfNull(explicitPayloads);
            SigningSourceIdentity[] identities = explicitPayloads.ToArray();
            if (identities.Any(identity => identity is null))
            {
                throw new ArgumentException("Explicit payload identities cannot contain null.", nameof(explicitPayloads));
            }
            _explicitPayloads = new HashSet<SigningSourceIdentity>(identities);
        }

        internal bool IsExplicitPayload(SigningSourceIdentity identity) => _explicitPayloads.Contains(identity);
    }

    internal interface IClickOncePayloadSigningHandler
    {
        bool CanSign(ClickOncePayloadSigningRequest request, SignOptions options);

        // The handler coordinates its work using the original source identity.
        // Actual result waits use coordinator.WaitAsync. It returns borrowed
        // coordinator results, never ownership of snapshots.
        Task<ClickOncePayloadSigningResult> SignAsync(
            ClickOncePayloadSigningRequest request,
            SignOptions options,
            SigningOperationCoordinator coordinator);
    }

    internal sealed class ClickOncePayloadSigningRequest
    {
        internal ClickOncePayloadSigningRequest(
            SigningFile source,
            DirectoryInfo sourceRoot,
            DirectoryInfo stagingRoot,
            ClickOnceStagedFile stagedFile)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(sourceRoot);
            ArgumentNullException.ThrowIfNull(stagingRoot);
            ArgumentNullException.ThrowIfNull(stagedFile);

            Source = source;
            SourceRoot = sourceRoot;
            StagingRoot = stagingRoot;
            StagedFile = stagedFile;
        }

        internal SigningFile Source { get; }
        internal DirectoryInfo SourceRoot { get; }
        internal DirectoryInfo StagingRoot { get; }
        internal ClickOnceStagedFile StagedFile { get; }
        internal FileInfo UpdateFile => StagedFile.FileInfoUpdateFile ?? StagedFile.Destination;
    }

    internal sealed class ClickOncePayloadSigningResult
    {
        internal ClickOncePayloadSigningResult(
            SigningOperationResult primary,
            IEnumerable<ClickOncePayloadSigningOutput>? additionalOutputs = null)
        {
            ArgumentNullException.ThrowIfNull(primary);
            ClickOncePayloadSigningOutput[] outputs = additionalOutputs?.ToArray() ??
                Array.Empty<ClickOncePayloadSigningOutput>();
            if (outputs.Any(output => output is null))
            {
                throw new ArgumentException(
                    "The output collection cannot contain a null element.",
                    nameof(additionalOutputs));
            }

            Primary = primary;
            AdditionalOutputs = Array.AsReadOnly(outputs);
        }

        internal SigningOperationResult Primary { get; }
        internal IReadOnlyList<ClickOncePayloadSigningOutput> AdditionalOutputs { get; }
    }

    // Each output names one coordinated primary and its destination in the
    // parent's layout. Siblings are not implicitly imported from its snapshot.
    internal sealed class ClickOncePayloadSigningOutput
    {
        internal ClickOncePayloadSigningOutput(
            SigningOperationPlan plan,
            SigningOperationResult result)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(result);
            Plan = plan;
            Result = result;
        }

        internal SigningOperationPlan Plan { get; }
        internal SigningOperationResult Result { get; }
    }
}

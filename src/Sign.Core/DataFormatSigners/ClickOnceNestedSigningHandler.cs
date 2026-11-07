// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Collections.Concurrent;
using System.Xml;

namespace Sign.Core
{
    // Invocation-local routing provenance, not a second artifact store. All
    // bytes and lifetimes remain owned by the operation coordinator.
    internal sealed class ClickOnceNestedSigningHandler : IClickOncePayloadSigningHandler
    {
        private readonly ClickOnceDeploymentManifestSigner _deployment;
        private readonly IClickOnceManifestReader _reader;
        private readonly IAggregatingDataFormatSigner _ordinary;
        private readonly AsyncLocal<ApplicationOperation?> _application = new();
        private readonly ConcurrentDictionary<string, Lazy<bool>> _applications = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<SigningSourceIdentity, IReadOnlyList<ClickOnceGraphOutput>> _outputs = new();

        internal ClickOnceNestedSigningHandler(
            ClickOnceDeploymentManifestSigner deployment,
            IClickOnceManifestReader reader,
            IAggregatingDataFormatSigner ordinary)
        {
            _deployment = deployment;
            _reader = reader;
            _ordinary = ordinary;
        }

        internal bool IsApplication(FileInfo source) =>
            _applications.GetOrAdd(source.FullName, _ => new Lazy<bool>(() =>
            {
                try
                {
                    using FileStream stream = source.OpenRead();
                    bool application = _reader.TryReadApplicationManifest(stream, out IApplicationManifest? manifest);
                    manifest?.Dispose();
                    return application;
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or InvalidOperationException or XmlException)
                {
                    return false;
                }
            })).Value;

        public bool CanSign(ClickOncePayloadSigningRequest request, SignOptions options)
        {
            string extension = request.UpdateFile.Extension;
            bool handled = extension.Equals(".application", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".vsto", StringComparison.OrdinalIgnoreCase) ||
                (extension.Equals(".manifest", StringComparison.OrdinalIgnoreCase) && IsApplication(request.Source.File));
            _application.Value?.Routes.Add(new Route(request, handled || _ordinary.HasSigningWork(request.UpdateFile, options)));
            return handled;
        }

        public async Task<ClickOncePayloadSigningResult> SignAsync(
            ClickOncePayloadSigningRequest request,
            SignOptions options,
            SigningOperationCoordinator coordinator)
        {
            ApplicationOperation? parent = _application.Value;
            bool application = request.UpdateFile.Extension.Equals(".manifest", StringComparison.OrdinalIgnoreCase);
            SigningOperationResult result = await coordinator.WaitForReadyOwnerAsync(coordinator.ExecuteArtifactAsync(
                request.Source.SourceIdentity,
                () => application
                    ? SignApplicationOwnerAsync(request.Source, options, coordinator)
                    : _deployment.SignOwnerAsync(request.Source, options, coordinator)));
            List<ClickOncePayloadSigningOutput> outputs = new();
            foreach (ClickOnceGraphOutput child in GetOutputs(request.Source.SourceIdentity))
            {
                FileInfo destination = new(Path.Combine(
                    request.UpdateFile.DirectoryName!, child.RelativePath));
                ClickOncePayloadSigningOutput output = new(
                    new SigningOperationPlan(child.Source, destination), child.Result);
                Route? direct = parent?.Routes.FirstOrDefault(route =>
                    route.Supported &&
                    route.Request.Source.SourceIdentity.Equals(output.Plan.Source.SourceIdentity) &&
                    string.Equals(route.Request.Source.File.FullName, output.Plan.Source.File.FullName, StringComparison.Ordinal) &&
                    FileInfoComparer.Instance.Equals(route.Request.StagedFile.Destination, destination) &&
                    (route.Request.StagedFile.FileInfoUpdateFile is not null) == child.IsMapped);
                if (direct is not null &&
                    coordinator.TryGetResult(direct.Request.Source.SourceIdentity, out Task<SigningOperationResult>? directTask) &&
                    directTask!.IsCompletedSuccessfully &&
                    ReferenceEquals(await coordinator.WaitForReadyOwnerAsync(directTask), output.Result))
                {
                    continue;
                }
                outputs.Add(output);
                parent?.Companions.Add(new Companion(output, child.IsMapped, request.Source, child.RelativePath));
            }
            return new ClickOncePayloadSigningResult(result, outputs);
        }

        internal async Task<SigningOperationArtifact> SignApplicationOwnerAsync(
            SigningFile input,
            SignOptions options,
            SigningOperationCoordinator coordinator,
            ClickOnceApplicationSigningPolicy policy = ClickOnceApplicationSigningPolicy.SignPayloads,
            bool updateManifest = true,
            ClickOnceApplicationSigningContext? context = null,
            ResolvedClickOncePublishLayout? layout = null,
            SigningFile? layoutOrigin = null)
        {
            ApplicationOperation? previous = _application.Value;
            ApplicationOperation operation = new();
            _application.Value = operation;
            SigningOperationArtifact? artifact = null;
            try
            {
                if (layout is null && updateManifest &&
                    _deployment.GetApplicationContext(input.SourceIdentity) is { } prepared)
                {
                    layout = prepared.Layout;
                    layoutOrigin = prepared.Origin;
                }
                ClickOnceApplicationManifestSigner signer = _deployment.CreateApplicationSigner();
                artifact = layout is null
                    ? await signer.SignOwnerAsync(input, options, coordinator, policy, updateManifest, context)
                    : await signer.SignOwnerAsync(input, options, coordinator, policy, layout, layoutOrigin, context);
                List<ClickOnceGraphOutput> outputs = new();
                foreach (FileInfo file in artifact.GetFiles().Where(file =>
                    !FileInfoComparer.Instance.Equals(file, artifact.PrimaryFile)))
                {
                    Companion? companion = operation.FindCompanion(file);
                    SigningFile source;
                    SigningOperationResult result;
                    bool mapped;
                    SigningFile? publicationOrigin = null;
                    string? originRelativePath = null;
                    if (companion is not null)
                    {
                        source = companion.Output.Plan.Source;
                        result = companion.Output.Result;
                        mapped = companion.IsMapped;
                        publicationOrigin = companion.PublicationOrigin;
                        originRelativePath = companion.OriginRelativePath;
                    }
                    else
                    {
                        Route route = operation.Routes.First(route =>
                            route.Supported && FileInfoComparer.Instance.Equals(route.Request.StagedFile.Destination, file));
                        source = route.Request.Source;
                        mapped = route.Request.StagedFile.FileInfoUpdateFile is not null;
                        if (!coordinator.TryGetResult(source.SourceIdentity, out Task<SigningOperationResult>? task))
                        {
                            throw new InvalidOperationException("A selected payload has no coordinated result.");
                        }
                        result = await coordinator.WaitForReadyOwnerAsync(task!);
                    }
                    outputs.Add(new ClickOnceGraphOutput(source,
                        Path.GetRelativePath(artifact.RootDirectory.FullName, file.FullName), result, mapped,
                        publicationOrigin, originRelativePath));
                }
                _outputs[input.SourceIdentity] = outputs;
                return artifact;
            }
            catch
            {
                artifact?.Dispose();
                throw;
            }
            finally
            {
                _application.Value = previous;
            }
        }

        internal IReadOnlyList<ClickOnceGraphOutput> GetOutputs(SigningSourceIdentity identity) =>
            _outputs.TryGetValue(identity, out IReadOnlyList<ClickOnceGraphOutput>? outputs)
                ? outputs
                : throw new InvalidOperationException("A nested manifest result has no selected-output provenance.");

        internal void SetOutputs(
            SigningSourceIdentity identity,
            IEnumerable<ClickOnceGraphOutput> outputs) =>
            _outputs[identity] = outputs.ToArray();

        private sealed class ApplicationOperation
        {
            internal List<Route> Routes { get; } = new();
            internal List<Companion> Companions { get; } = new();

            internal Companion? FindCompanion(FileInfo file) => Companions.FirstOrDefault(companion =>
                FileInfoComparer.Instance.Equals(companion.Output.Plan.Output, file));
        }

        private sealed record Companion(
            ClickOncePayloadSigningOutput Output,
            bool IsMapped,
            SigningFile PublicationOrigin,
            string OriginRelativePath);

        private sealed record Route(ClickOncePayloadSigningRequest Request, bool Supported);
    }
}

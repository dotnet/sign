// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.ExceptionServices;

namespace Sign.Core
{
    internal sealed class ClickOnceDeploymentSigningContext : IDisposable
    {
        private readonly Dictionary<SigningSourceIdentity, ResolvedClickOncePublishLayout> _deployments = new();
        private readonly Dictionary<SigningSourceIdentity, ApplicationContext> _applications = new();
        private readonly List<IApplicationManifest> _standaloneManifests = new();
        private readonly List<ResolvedClickOncePublishLayout> _deploymentLayouts = new();
        private readonly ConcurrentQueue<SigningFile> _executionSources = new();

        internal void TrackSource(SigningFile source) => _executionSources.Enqueue(source);

        internal IReadOnlyList<SigningFile> GetExecutionSources() => _executionSources.ToArray();

        internal static ClickOnceDeploymentSigningContext Create(
            IReadOnlyList<SigningOperationPlan> deployments,
            IReadOnlyList<SigningFile> explicitApplications,
            ClickOnceDeploymentPublishLayoutResolver resolver,
            ClickOnceApplicationPublishLayoutResolver applicationResolver,
            bool signDependencies,
            Func<FileInfo, DirectoryInfo, bool> isSelected,
            Func<FileInfo, bool> isApplication,
            Action<int>? observeIteration = null)
        {
            ClickOnceDeploymentSigningContext context = new();
            try
            {
                Dictionary<SigningSourceIdentity, DeploymentCandidate> deploymentCandidates = new();
                Dictionary<SigningSourceIdentity, ApplicationCandidate> applicationCandidates = new();
                Queue<Candidate> pending = new();
                HashSet<Candidate> queued = new();
                HashSet<ApplicationCandidate> replacements = new();
                HashSet<string> selectionStates = new(StringComparer.Ordinal);
                Dictionary<ApplicationContext, int> contextIds = new();
                int iterations = 0;
                IReadOnlySet<SigningSourceIdentity> explicitIdentities =
                    explicitApplications.Select(application => application.SourceIdentity).ToHashSet();
                foreach (SigningOperationPlan deployment in deployments)
                {
                    DeploymentCandidate candidate = GetDeployment(deployment.Source);
                    candidate.Explicit = true;
                    Enqueue(candidate);
                }
                foreach (SigningFile application in explicitApplications)
                {
                    ApplicationCandidate candidate = GetApplication(application);
                    candidate.Explicit = true;
                    Enqueue(candidate);
                }
                while (pending.Count != 0 || replacements.Count != 0)
                {
                    observeIteration?.Invoke(++iterations);
                    if (pending.Count == 0)
                    {
                        Dictionary<ApplicationCandidate, ApplicationContext> choices = new();
                        foreach (ApplicationCandidate replacement in replacements)
                        {
                            ApplicationContext? proposed = GetAuthorities(replacement).FirstOrDefault()?.Context
                                ?? replacement.Standalone;
                            if (proposed is not null && !ReferenceEquals(replacement.Selected, proposed))
                            {
                                choices.Add(replacement, proposed);
                            }
                        }
                        replacements.Clear();
                        if (choices.Count == 0)
                        {
                            continue;
                        }
                        string state = string.Join(",", applicationCandidates.Values.Select(application =>
                            $"{ContextId(application.Selected)}:{application.Inspected}")) + "|" +
                            string.Join(",", deploymentCandidates.Values.Select(deployment => deployment.Inspected));
                        if (!selectionStates.Add(state))
                        {
                            ThrowAmbiguity(choices.Keys);
                        }
                        IReadOnlyDictionary<ApplicationCandidate, ApplicationContext> ready =
                            SupportedReplacements(choices);
                        foreach (ApplicationCandidate replacement in choices.Keys.Except(ready.Keys))
                        {
                            replacements.Add(replacement);
                        }
                        foreach ((ApplicationCandidate replacement, ApplicationContext proposed) in ready)
                        {
                            Select(replacement, proposed);
                        }
                        continue;
                    }
                    Candidate candidate = pending.Dequeue();
                    queued.Remove(candidate);
                    candidate.Active = HasSupport(candidate);
                    if (candidate is DeploymentCandidate deployment)
                    {
                        if (deployment.Active && !deployment.Inspected)
                        {
                            deployment.Inspected = true;
                            try
                            {
                                ResolveDeployment(deployment);
                            }
                            catch (ClickOncePublishLayoutResolutionException exception) when (!deployment.Explicit)
                            {
                                // Conditional route outcomes are validated only if their support survives.
                                deployment.Failure = ExceptionDispatchInfo.Capture(exception);
                            }
                        }
                        if (deployment.Application is not null)
                        {
                            Enqueue(deployment.Application);
                        }
                        continue;
                    }
                    ApplicationCandidate application = (ApplicationCandidate)candidate;
                    ApplicationContext? selected = null;
                    if (application.Active)
                    {
                        selected = GetAuthorities(application).FirstOrDefault()?.Context;
                        if (selected is null)
                        {
                            if (!application.Inspected)
                            {
                                application.Inspected = true;
                                try
                                {
                                    if (applicationResolver.TryResolve(application.Source.File,
                                        out ResolvedClickOncePublishLayout? standalone))
                                    {
                                        context._standaloneManifests.Add(standalone.Application.Manifest);
                                        application.Standalone = new(
                                            standalone, application.Source, Array.Empty<PayloadContext>());
                                    }
                                }
                                catch (ClickOncePublishLayoutResolutionException exception)
                                    when (exception.IsMissingPayload || !application.Explicit)
                                {
                                    application.Failure = ExceptionDispatchInfo.Capture(exception);
                                }
                            }
                            selected = application.Standalone;
                        }
                    }
                    if (ReferenceEquals(application.Selected, selected))
                    {
                        continue;
                    }
                    if (application.Selected is not null && selected is not null)
                    {
                        replacements.Add(application);
                        continue;
                    }
                    Select(application, selected);
                }

                void Select(ApplicationCandidate application, ApplicationContext? selected)
                {
                    application.Selected = selected;
                    application.Generation++;
                    Candidate[] previousTargets = application.Routes.Select(route => route.Target).ToArray();
                    foreach (SelectionRoute route in application.Routes)
                    {
                        route.Target.Support.Remove(route);
                    }
                    application.Routes.Clear();
                    foreach (Candidate target in previousTargets)
                    {
                        SupportChanged(target);
                    }
                    if (selected is not null && signDependencies)
                    {
                        Discover(application, selected);
                    }
                }

                foreach (DeploymentCandidate deployment in deploymentCandidates.Values
                    .Where(candidate => candidate.Active)
                    .OrderBy(candidate => candidate.Source.File.FullName, StringComparer.Ordinal))
                {
                    deployment.Failure?.Throw();
                    context._deployments.Add(deployment.Source.SourceIdentity, deployment.Layout!);
                }
                foreach (ApplicationCandidate application in applicationCandidates.Values
                    .Where(candidate => candidate.Active)
                    .OrderBy(candidate => candidate.Source.File.FullName, StringComparer.Ordinal))
                {
                    DeploymentCandidate[] authorities = GetAuthorities(application).ToArray();
                    if (authorities.Length == 0)
                    {
                        application.Failure?.Throw();
                    }
                    else
                    {
                        ApplicationContext first = authorities[0].Context!;
                        if (authorities.Any(authority => !Compatible(first, authority.Context!)))
                        {
                            throw new ClickOncePublishLayoutResolutionException(string.Format(
                                CultureInfo.CurrentCulture,
                                Resources.ClickOnceDeploymentManifestConflictingApplicationContexts,
                                application.Source.File.FullName,
                                string.Join(", ", authorities.Select(authority => authority.Source.File.FullName)
                                    .OrderBy(path => path, StringComparer.Ordinal))),
                                authorities.SelectMany(authority => authority.Layout!.Diagnostics));
                        }
                    }
                    if (application.Selected is not null)
                    {
                        context._applications.Add(application.Source.SourceIdentity, application.Selected);
                    }
                }
                return context;

                void ResolveDeployment(DeploymentCandidate deployment)
                {
                    SigningFile source = deployment.Source;
                    ResolvedClickOncePublishLayout layout = resolver.Resolve(source.File, resolvePayloads: signDependencies);
                    deployment.Layout = layout;
                    context._deploymentLayouts.Add(layout);
                    SigningFile applicationSource = source.GetSibling(
                        ClickOnceApplicationManifestSigner.BindOriginalSource(
                            layout.Application.Source, source.File.Directory!), source.File.Directory!);
                    if (!signDependencies && !explicitIdentities.Contains(applicationSource.SourceIdentity))
                    {
                        return;
                    }
                    ResolvedClickOnceApplication applicationNode = new(
                        applicationSource.File, layout.Application.Manifest, layout.Application.Payloads);
                    if (!signDependencies)
                    {
                        List<ClickOnceManifestDiagnostic> diagnostics = layout.Diagnostics.ToList();
                        IReadOnlyList<ResolvedClickOncePayload> payloads = new ClickOncePayloadResolver()
                            .ResolveForDeployment(applicationSource.File, applicationNode.Manifest,
                                source.File.Directory!, layout.Deployment!.Manifest.MapFileExtensions, diagnostics);
                        applicationNode = new(applicationSource.File, applicationNode.Manifest, payloads);
                    }
                    ResolvedClickOncePublishLayout applicationLayout = new(
                        null, applicationNode, Array.Empty<ResolvedClickOnceAdjacentExecutable>(), layout.Diagnostics);
                    PayloadContext[] provenance = applicationNode.Payloads.Select(payload =>
                    {
                        FileInfo original = ClickOnceApplicationManifestSigner.BindOriginalSource(
                            payload.Source, source.File.Directory!);
                        return new PayloadContext(
                            payload.TargetPath,
                            original.FullName,
                            source.GetSibling(original, source.File.Directory!).SourceIdentity,
                            payload.IsFileExtensionMapped,
                            isSelected(original, source.File.Directory!));
                    }).OrderBy(payload => payload.TargetPath, StringComparer.Ordinal)
                        .ThenBy(payload => payload.SourcePath, StringComparer.Ordinal).ToArray();
                    deployment.Context = new(applicationLayout, source, provenance);
                    deployment.Application = GetApplication(applicationSource);
                    deployment.Application.Deployments.Add(deployment);
                    SupportChanged(deployment.Application);
                }

                void Discover(ApplicationCandidate application, ApplicationContext selected)
                {
                    foreach ((SigningFile source, bool deployment) in SelectedChildren(selected))
                    {
                        Candidate target = deployment ? GetDeployment(source) : GetApplication(source);
                        SelectionRoute route = new(application, application.Generation, target);
                        application.Routes.Add(route);
                        target.Support.Add(route);
                        SupportChanged(target);
                    }
                }

                IEnumerable<(SigningFile Source, bool Deployment)> SelectedChildren(ApplicationContext selected)
                {
                    SigningFile origin = selected.Origin;
                    foreach (ResolvedClickOncePayload payload in selected.Layout.Application.Payloads
                        .OrderBy(payload => payload.TargetPath, StringComparer.Ordinal))
                    {
                        FileInfo source = ClickOnceApplicationManifestSigner.BindOriginalSource(
                            payload.Source, origin.File.Directory!);
                        if (!isSelected(source, origin.File.Directory!))
                        {
                            continue;
                        }
                        string extension = Path.GetExtension(payload.TargetPath);
                        bool deployment = extension.Equals(".application", StringComparison.OrdinalIgnoreCase) ||
                            extension.Equals(".vsto", StringComparison.OrdinalIgnoreCase);
                        if (deployment || extension.Equals(".manifest", StringComparison.OrdinalIgnoreCase) &&
                            isApplication(source))
                        {
                            yield return (origin.GetSibling(source, origin.File.Directory!), deployment);
                        }
                    }
                }

                IReadOnlyDictionary<ApplicationCandidate, ApplicationContext> SupportedReplacements(
                    IReadOnlyDictionary<ApplicationCandidate, ApplicationContext> choices)
                {
                    Dictionary<ApplicationCandidate, HashSet<SigningSourceIdentity>> targets = choices.ToDictionary(
                        choice => choice.Key,
                        choice => SelectedChildren(choice.Value).Select(child => child.Source.SourceIdentity).ToHashSet());
                    Dictionary<ApplicationCandidate, ApplicationContext> ready = new();
                    foreach ((ApplicationCandidate application, ApplicationContext proposed) in choices)
                    {
                        DeploymentCandidate[] authorities = GetAuthorities(application).ToArray();
                        if (authorities.Length == 0 || authorities.Any(authority =>
                            Compatible(authority.Context!, proposed) &&
                            IsSupported(authority, application, new(), targets)))
                        {
                            ready.Add(application, proposed);
                        }
                    }
                    if (ready.Count == 0)
                    {
                        ThrowAmbiguity(choices.Keys);
                    }
                    return ready;
                }

                void ThrowAmbiguity(IEnumerable<ApplicationCandidate> ambiguous) =>
                    throw new ClickOncePublishLayoutResolutionException(string.Format(
                        CultureInfo.CurrentCulture,
                        Resources.ClickOnceDeploymentManifestAmbiguousApplicationContexts,
                        string.Join(", ", ambiguous.Select(application => application.Source.File.FullName)
                            .OrderBy(path => path, StringComparer.Ordinal))),
                        ambiguous.SelectMany(GetAuthorities).SelectMany(authority => authority.Layout!.Diagnostics));

                int ContextId(ApplicationContext? selected)
                {
                    if (selected is null)
                    {
                        return -1;
                    }
                    if (!contextIds.TryGetValue(selected, out int id))
                    {
                        id = contextIds.Count;
                        contextIds.Add(selected, id);
                    }
                    return id;
                }

                IEnumerable<DeploymentCandidate> GetAuthorities(ApplicationCandidate application) =>
                    application.Deployments.Where(deployment =>
                        deployment.Context is not null && HasSupport(deployment, application));

                bool HasSupport(Candidate candidate, ApplicationCandidate? excluded = null) =>
                    IsSupported(candidate, excluded, new());

                bool IsSupported(Candidate candidate, ApplicationCandidate? excluded, HashSet<Candidate> visited,
                    IReadOnlyDictionary<ApplicationCandidate, HashSet<SigningSourceIdentity>>? replacements = null)
                {
                    if (ReferenceEquals(candidate, excluded))
                    {
                        return false;
                    }
                    if (candidate.Explicit)
                    {
                        return true;
                    }
                    if (!visited.Add(candidate))
                    {
                        return false;
                    }
                    return candidate.Support.Any(route =>
                        route.Generation == route.Application.Generation &&
                        route.Application.Selected is not null &&
                        (replacements is null ||
                            !replacements.TryGetValue(route.Application, out HashSet<SigningSourceIdentity>? targets) ||
                            targets.Contains(route.Target.Source.SourceIdentity)) &&
                        IsSupported(route.Application, excluded, visited, replacements)) ||
                        candidate is ApplicationCandidate application &&
                        application.Deployments.Any(deployment =>
                            IsSupported(deployment, excluded, visited, replacements));
                }

                void SupportChanged(Candidate first)
                {
                    Queue<Candidate> affected = new();
                    HashSet<Candidate> visited = new();
                    affected.Enqueue(first);
                    while (affected.TryDequeue(out Candidate? candidate))
                    {
                        if (!visited.Add(candidate))
                        {
                            continue;
                        }
                        Enqueue(candidate);
                        if (candidate is ApplicationCandidate application)
                        {
                            foreach (SelectionRoute route in application.Routes)
                            {
                                affected.Enqueue(route.Target);
                            }
                        }
                        else if (((DeploymentCandidate)candidate).Application is ApplicationCandidate selected)
                        {
                            affected.Enqueue(selected);
                        }
                    }
                }

                void Enqueue(Candidate candidate)
                {
                    if (queued.Add(candidate))
                    {
                        pending.Enqueue(candidate);
                    }
                }

                ApplicationCandidate GetApplication(SigningFile source)
                {
                    if (!applicationCandidates.TryGetValue(source.SourceIdentity, out ApplicationCandidate? candidate))
                    {
                        candidate = new(source);
                        applicationCandidates.Add(source.SourceIdentity, candidate);
                    }
                    return candidate;
                }

                DeploymentCandidate GetDeployment(SigningFile source)
                {
                    if (!deploymentCandidates.TryGetValue(source.SourceIdentity, out DeploymentCandidate? candidate))
                    {
                        candidate = new(source);
                        deploymentCandidates.Add(source.SourceIdentity, candidate);
                    }
                    return candidate;
                }

                static bool Compatible(ApplicationContext left, ApplicationContext right) =>
                    string.Equals(left.Layout.Application.Source.FullName, right.Layout.Application.Source.FullName,
                        StringComparison.Ordinal) && left.Provenance.SequenceEqual(right.Provenance);
            }
            catch
            {
                context.Dispose();
                throw;
            }
        }

        internal ResolvedClickOncePublishLayout? GetDeployment(SigningSourceIdentity identity) =>
            _deployments.GetValueOrDefault(identity);

        internal ApplicationContext? GetApplication(SigningSourceIdentity identity) =>
            _applications.GetValueOrDefault(identity);

        internal IEnumerable<SigningFile> GetSources()
        {
            foreach ((SigningSourceIdentity identity, ResolvedClickOncePublishLayout layout) in _deployments)
            {
                SigningFile deployment = new(layout.Deployment!.Source, identity);
                foreach (SigningFile source in GetLayoutSources(deployment, layout))
                {
                    yield return source;
                }
            }
            foreach (ApplicationContext application in _applications.Values)
            {
                foreach (SigningFile source in GetLayoutSources(application.Origin, application.Layout))
                {
                    yield return source;
                }
            }
        }

        internal static IEnumerable<SigningFile> GetLayoutSources(
            SigningFile origin, ResolvedClickOncePublishLayout layout) =>
            layout.Application.Payloads.Select(payload => payload.Source)
                .Concat(layout.AdjacentExecutables.Select(executable => executable.Source))
                .Append(layout.Application.Source)
                .Concat(layout.Deployment is null ? Array.Empty<FileInfo>() : new[] { layout.Deployment.Source })
                .Select(file => origin.GetSibling(
                    ClickOnceApplicationManifestSigner.BindOriginalSource(file, origin.File.Directory!),
                    origin.File.Directory!));

        public void Dispose()
        {
            foreach (ResolvedClickOncePublishLayout layout in _deploymentLayouts)
            {
                layout.Deployment!.Manifest.Dispose();
                layout.Application.Manifest.Dispose();
            }
            foreach (IApplicationManifest manifest in _standaloneManifests)
            {
                manifest.Dispose();
            }
        }

        private abstract class Candidate
        {
            protected Candidate(SigningFile source) => Source = source;
            internal SigningFile Source { get; }
            internal bool Explicit { get; set; }
            internal bool Active { get; set; }
            internal bool Inspected { get; set; }
            internal ExceptionDispatchInfo? Failure { get; set; }
            internal List<SelectionRoute> Support { get; } = new();
        }

        private sealed class DeploymentCandidate : Candidate
        {
            internal DeploymentCandidate(SigningFile source) : base(source) { }
            internal ResolvedClickOncePublishLayout? Layout { get; set; }
            internal ApplicationContext? Context { get; set; }
            internal ApplicationCandidate? Application { get; set; }
        }

        private sealed class ApplicationCandidate : Candidate
        {
            internal ApplicationCandidate(SigningFile source) : base(source) { }
            internal ApplicationContext? Standalone { get; set; }
            internal ApplicationContext? Selected { get; set; }
            internal int Generation { get; set; }
            internal List<DeploymentCandidate> Deployments { get; } = new();
            internal List<SelectionRoute> Routes { get; } = new();
        }

        private sealed record SelectionRoute(ApplicationCandidate Application, int Generation, Candidate Target);

        internal sealed class ApplicationContext
        {
            internal ApplicationContext(
                ResolvedClickOncePublishLayout layout,
                SigningFile origin,
                PayloadContext[] provenance)
            {
                Layout = layout;
                Origin = origin;
                Provenance = provenance;
            }

            internal ResolvedClickOncePublishLayout Layout { get; }
            internal SigningFile Origin { get; }
            internal PayloadContext[] Provenance { get; }
        }

        internal sealed record PayloadContext(
            string TargetPath,
            string SourcePath,
            SigningSourceIdentity Identity,
            bool Mapped,
            bool Selected);
    }
}

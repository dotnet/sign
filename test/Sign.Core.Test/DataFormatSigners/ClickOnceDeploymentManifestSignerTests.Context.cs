// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NSubstitute;
using Sign.Core;
using Xunit;

namespace Sign.Core.Test
{
    public partial class ClickOnceDeploymentManifestSignerTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Create_WhenMappedAuthorityReplacesConditionalCandidates_ReadsCandidatesOnceAndDisposesAllParsedLayouts(bool reverse)
        {
            await using Harness harness = new();
            FileInfo payload = harness.File(@"Other\Good\payload.dll", "payload");
            harness.Application(@"Other\Good\Good.manifest", payload.Name);
            FileInfo unmapped = harness.Deployment(@"Other\Child.application", "Missing.manifest");
            FileInfo mapped = harness.Deployment(@"Other\Child.application.deploy", @"Good\Good.manifest");
            FileInfo inner = harness.Application(@"Other\Inner.manifest", "Child.application");
            FileInfo deployment = harness.Deployment("B.application", @"Other\Inner.manifest", mapped: true);
            FileInfo outer = harness.Application("Outer.manifest", deployment.Name);
            RecordingReader reader = new();
            ClickOncePayloadResolver payloadResolver = new();
            ClickOnceDeploymentPublishLayoutResolver deploymentResolver = new(reader, payloadResolver);
            ClickOnceApplicationPublishLayoutResolver applicationResolver = new(reader, payloadResolver);
            SigningFile[] roots = new[] { inner, outer }.Select(file => SigningFile.Capture(file)).ToArray();
            if (reverse)
            {
                Array.Reverse(roots);
            }

            using (ClickOnceDeploymentSigningContext context = ClickOnceDeploymentSigningContext.Create(
                Array.Empty<SigningOperationPlan>(), roots, deploymentResolver, applicationResolver,
                signDependencies: true, (_, _) => true, _ => false))
            {
                Assert.Null(context.GetDeployment(SigningFile.Capture(unmapped).SourceIdentity));
                Assert.NotNull(context.GetDeployment(SigningFile.Capture(mapped).SourceIdentity));
                Assert.Equal(deployment.FullName, context.GetApplication(roots.Single(
                    root => root.File.FullName == inner.FullName).SourceIdentity)!.Origin.File.FullName);
                Assert.All(reader.DeploymentReadsByPath.Values, count => Assert.Equal(1, count));
                Assert.Equal(1, reader.DeploymentReadsByPath[mapped.FullName]);
                Assert.Equal(1, reader.DeploymentReadsByPath[deployment.FullName]);
                Assert.Equal(3, reader.DeploymentReads);
                Assert.Equal(4, reader.ApplicationReads);
                Assert.Equal(1, reader.DeploymentDisposals);
                Assert.Equal(0, reader.ApplicationDisposals);
                Assert.Empty(harness.Calls);
            }
            Assert.Equal(reader.DeploymentReads, reader.DeploymentDisposals);
            Assert.Equal(reader.ApplicationReads, reader.ApplicationDisposals);
            Assert.All(reader.ParsedManifests, manifest => manifest.Received(1).Dispose());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Resolve_WhenSelectedPayloadIsMissingOrUnsafe_DisposesDeploymentAndApplicationManifests(bool missingPayload)
        {
            await using Harness harness = new();
            FileInfo application = missingPayload
                ? harness.Application("App.manifest", "missing.dll")
                : harness.Application("App.manifest", @"..\outside.dll");
            FileInfo deployment = harness.Deployment("App.application", application.Name);
            RecordingReader reader = new();
            ClickOnceDeploymentPublishLayoutResolver resolver = new(reader, new ClickOncePayloadResolver());

            Assert.Throws<ClickOncePublishLayoutResolutionException>(() => resolver.Resolve(deployment));

            Assert.Equal(1, reader.DeploymentReads);
            Assert.Equal(1, reader.ApplicationReads);
            Assert.Equal(1, reader.DeploymentDisposals);
            Assert.Equal(1, reader.ApplicationDisposals);
            Assert.All(reader.ParsedManifests, manifest => manifest.Received(1).Dispose());
            Assert.Empty(harness.Calls);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public async Task Create_WhenCollectiveAuthoritiesAreReplaced_TerminatesWithinBoundAndReadsCandidatesOnce(
            bool reverse, bool anchored)
        {
            await using Harness harness = new();
            FileInfo a = harness.Application("A.manifest", "ToB.application", "ToC.application");
            FileInfo b = harness.Application("B.manifest", "ToA.application");
            FileInfo c = harness.Application("C.manifest", "ToA.application");
            FileInfo anchor = harness.Deployment("ToA.application", a.Name, mapped: true);
            harness.Deployment("ToB.application", b.Name, mapped: true);
            harness.Deployment("ToC.application", c.Name, mapped: true);
            foreach (string name in new[] { "A", "B", "C" })
            {
                harness.Application($"Leaf{name}.manifest");
                harness.Deployment($"To{name}.application.deploy", $"Leaf{name}.manifest");
            }
            SigningFile[] roots = new[] { a, b, c }.Select(file => SigningFile.Capture(file)).ToArray();
            if (reverse)
            {
                Array.Reverse(roots);
            }
            SigningOperationPlan[] deployments = anchored
                ? new[] { new SigningOperationPlan(SigningFile.Capture(anchor), anchor) }
                : Array.Empty<SigningOperationPlan>();
            RecordingReader reader = new();
            int iterations = 0;
            ClickOnceDeploymentSigningContext Create() => ClickOnceDeploymentSigningContext.Create(
                deployments, roots, new(reader, new()), new(reader, new()), true,
                (_, _) => true, _ => false, value => iterations = value);
            if (anchored)
            {
                using ClickOnceDeploymentSigningContext context = Create();
                Assert.NotNull(context.GetDeployment(SigningFile.Capture(anchor).SourceIdentity));
            }
            else
            {
                ClickOncePublishLayoutResolutionException exception =
                    Assert.Throws<ClickOncePublishLayoutResolutionException>(() => Create());
                Assert.Equal(string.Format(System.Globalization.CultureInfo.CurrentCulture,
                    Resources.ClickOnceDeploymentManifestAmbiguousApplicationContexts,
                    string.Join(", ", new[] { a.FullName, b.FullName, c.FullName })), exception.Message);
            }
            Assert.InRange(iterations, 1, 80);
            Assert.All(reader.DeploymentReadsByPath.Values, count => Assert.Equal(1, count));
            Assert.All(reader.ParsedManifests, manifest => manifest.Received(1).Dispose());
            Assert.Empty(harness.Calls);
            Assert.All(roots, root => Assert.False(harness.Coordinator.TryGetResult(root.SourceIdentity, out _)));
        }
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using NSubstitute;
using Sign.TestInfrastructure;

namespace Sign.Core.Test
{
    public sealed partial class ClickOnceDeploymentManifestSignerTests
    {
        [Theory]
        [InlineData(1, false, false)]
        [InlineData(4, false, false)]
        [InlineData(1, true, false)]
        [InlineData(4, true, false)]
        [InlineData(1, false, true)]
        [InlineData(4, false, true)]
        [InlineData(1, true, true)]
        [InlineData(4, true, true)]
        public async Task ExecuteAsync_WhenPublicationOverlapsExcludedGraphSource_RejectsBeforeFinalMutation(
            int concurrency, bool applicationPublisher, bool nestedProtectedSource)
        {
            await using Harness harness = new();
            FileInfo firstPayload = harness.File(@"first\v1\payload.dll", "first bytes");
            FileInfo firstApplication = harness.Application(@"first\v1\App.manifest", firstPayload.Name);
            FileInfo publisher = applicationPublisher ? firstApplication :
                harness.Deployment(@"first\A.application", @"v1\App.manifest");
            FileInfo protectedPayload = harness.File(@"second\v1\payload.dll", "second bytes");
            FileInfo protectedApplication = harness.Application(@"second\v1\B.manifest", protectedPayload.Name);
            FileInfo protectedOwner;
            if (nestedProtectedSource)
            {
                harness.Application(@"second\Parent.manifest", @"v1\B.manifest");
                protectedOwner = harness.Deployment(@"second\Parent.application", "Parent.manifest");
            }
            else
            {
                protectedOwner = protectedApplication;
            }
            // Distinct selection roots allow the first payload while excluding B's.
            Matcher include = new();
            include.AddInclude("**/*");
            Matcher exclude = new();
            exclude.AddInclude("payload.dll");
            if (applicationPublisher)
            {
                firstApplication = harness.Application(@"first\App.manifest", @"v1\payload.dll");
                publisher = firstApplication;
            }
            if (nestedProtectedSource)
            {
                include = new();
                include.AddInclude("v1/*");
                include.AddInclude("Parent.manifest");
            }
            harness.Options = new(null, null, null, null, HashAlgorithmName.SHA256, HashAlgorithmName.SHA256,
                new Uri("https://timestamp.test"), include, exclude, true);
            FileInfo output = new(Path.Combine(harness.Source.FullName, "second", publisher.Name));
            harness.File(Path.GetRelativePath(harness.Source.FullName, output.FullName), "manifest sentinel");
            FileInfo ordinary = harness.File("ordinary.dll", "ordinary");
            FileInfo ordinaryOutput = harness.File(@"output\ordinary.dll", "ordinary sentinel");
            SigningOperationPlan[] plans =
            {
                new(SigningFile.Capture(ordinary), ordinaryOutput),
                new(SigningFile.Capture(publisher), output),
                new(SigningFile.Capture(protectedOwner), protectedOwner)
            };
            Dictionary<string, byte[]> before = Snapshot(harness);
            harness.BeforeSign = _ => AssertUnchanged(harness, before);

            SigningException failure = await Assert.ThrowsAsync<SigningException>(() =>
                harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, concurrency));

            Assert.Equal(string.Format(CultureInfo.CurrentCulture, Resources.ClickOnceOwnershipCollision,
                protectedPayload.FullName, protectedPayload.FullName, publisher.FullName, firstPayload.FullName),
                failure.Message);
            AssertUnchanged(harness, before);
            Assert.NotEmpty(harness.Calls);
            await harness.Coordinator.DisposeAsync();
            AssertStagingDeleted(harness);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(4)]
        public async Task ExecuteAsync_WhenPublishingSameSourceInPlaceAndSharedResults_AllowsReuse(int concurrency)
        {
            await using Harness harness = new();
            FileInfo payload = harness.File(@"v1\payload.dll", "payload");
            FileInfo application = harness.Application(@"v1\App.manifest", payload.Name);
            FileInfo deployment = harness.Deployment("A.application", @"v1\App.manifest");
            SigningOperationPlan[] plans =
            {
                new(SigningFile.Capture(payload), payload),
                new(SigningFile.Capture(application), application),
                new(SigningFile.Capture(deployment), deployment),
                new(SigningFile.Capture(deployment), deployment)
            };

            await harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, concurrency);

            Assert.Equal("payload signed", System.IO.File.ReadAllText(payload.FullName));
            Assert.Equal(1, harness.Calls.Count(name => name.EndsWith(".dll", StringComparison.Ordinal)));
            harness.AssertPayloadHash(application, payload);
            harness.AssertHash(deployment, application);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(4)]
        public async Task ExecuteAsync_WhenNestedSelectionChangesAfterPreflight_ProtectsNewlyResolvedExcludedSources(int concurrency)
        {
            IFileMatcher matcher = Substitute.For<IFileMatcher>();
            FileMatcher realMatcher = new();
            bool execution = false;
            matcher.EnumerateMatches(Arg.Any<DirectoryInfoBase>(), Arg.Any<Matcher>()).Returns(call =>
                realMatcher.EnumerateMatches(call.Arg<DirectoryInfoBase>(), call.Arg<Matcher>())
                    .Where(file => !execution || file.Name != "B.manifest").ToArray());
            await using Harness harness = new(fileMatcher: matcher);
            FileInfo ordinary = harness.File("ordinary.dll", "ordinary");
            FileInfo firstPayload = harness.File(@"first\v1\payload.dll", "first bytes");
            harness.Application(@"first\v1\App.manifest", firstPayload.Name);
            FileInfo publisher = harness.Deployment(@"first\A.application", @"v1\App.manifest");
            FileInfo protectedPayload = harness.File(@"second\v1\payload.dll", "second bytes");
            harness.Application(@"second\v1\B.manifest", protectedPayload.Name);
            harness.Application(@"second\Parent.manifest", @"v1\B.manifest");
            FileInfo protectedOwner = harness.Deployment(@"second\Parent.application", "Parent.manifest");
            Matcher exclude = new();
            exclude.AddInclude("payload.dll");
            exclude.AddInclude("v1/B.manifest");
            harness.Options = SelectionOptions(harness, null, exclude);
            SigningOperationPlan[] plans =
            {
                new(SigningFile.Capture(ordinary), harness.Output(ordinary.Name)),
                new(SigningFile.Capture(publisher), new FileInfo(Path.Combine(harness.Source.FullName, "second", publisher.Name))),
                new(SigningFile.Capture(protectedOwner), protectedOwner)
            };
            Dictionary<string, byte[]> before = Snapshot(harness);
            harness.BeforeSign = _ =>
            {
                AssertUnchanged(harness, before);
                execution = true;
            };

            SigningException failure = await Assert.ThrowsAsync<SigningException>(() =>
                harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, concurrency));

            Assert.Contains(protectedPayload.FullName, failure.Message);
            AssertUnchanged(harness, before);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(4)]
        public async Task ExecuteAsync_WhenOrdinaryOutputOverlapsNewlyResolvedSource_RejectsBeforeFinalMutation(int concurrency)
        {
            IFileMatcher matcher = Substitute.For<IFileMatcher>();
            FileMatcher realMatcher = new();
            bool execution = false;
            matcher.EnumerateMatches(Arg.Any<DirectoryInfoBase>(), Arg.Any<Matcher>()).Returns(call =>
                realMatcher.EnumerateMatches(call.Arg<DirectoryInfoBase>(), call.Arg<Matcher>())
                    .Where(file => !execution || file.Name != "B.manifest").ToArray());
            await using Harness harness = new(fileMatcher: matcher);
            FileInfo ordinary = harness.File("ordinary.dll", "ordinary");
            FileInfo protectedPayload = harness.File(@"second\v1\payload.dll", "second bytes");
            harness.Application(@"second\v1\B.manifest", protectedPayload.Name);
            harness.Application(@"second\Parent.manifest", @"v1\B.manifest");
            FileInfo protectedOwner = harness.Deployment(@"second\Parent.application", "Parent.manifest");
            Matcher exclude = new();
            exclude.AddInclude("payload.dll");
            exclude.AddInclude("v1/B.manifest");
            harness.Options = SelectionOptions(harness, null, exclude);
            SigningOperationPlan[] plans =
            {
                new(SigningFile.Capture(ordinary), protectedPayload),
                new(SigningFile.Capture(protectedOwner), protectedOwner)
            };
            Dictionary<string, byte[]> before = Snapshot(harness);
            harness.BeforeSign = _ =>
            {
                AssertUnchanged(harness, before);
                execution = true;
            };

            SigningException failure = await Assert.ThrowsAsync<SigningException>(() =>
                harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, concurrency));

            Assert.Contains(protectedPayload.FullName, failure.Message);
            AssertUnchanged(harness, before);
        }

        [Theory]
        [InlineData(1, false)]
        [InlineData(4, false)]
        [InlineData(1, true)]
        [InlineData(4, true)]
        public async Task ExecuteAsync_WhenOwnerFaultsOrCancels_DoesNotPublishOrLeakStaging(int concurrency, bool canceled)
        {
            await using Harness harness = new();
            harness.File("payload.dll", "payload");
            FileInfo application = harness.Application("App.manifest", "payload.dll");
            FileInfo deployment = harness.Deployment("A.application", application.Name);
            Dictionary<string, byte[]> before = Snapshot(harness);
            Exception expected = canceled ? new OperationCanceledException("signing cancellation") :
                new InvalidOperationException("signing failure");
            harness.BeforeSign = _ => throw expected;

            Exception? actual = await Record.ExceptionAsync(() => harness.Pipeline.ExecuteAsync(
                new[] { new SigningOperationPlan(SigningFile.Capture(deployment), harness.Output(deployment.Name)) },
                harness.Options, harness.Coordinator, concurrency));

            Assert.Same(expected, actual);
            AssertUnchanged(harness, before);
            await harness.Coordinator.DisposeAsync();
            AssertStagingDeleted(harness);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(4)]
        public async Task ExecuteAsync_WhenPublicationOverlapsExcludedAdjacentExecutable_RejectsBeforeFinalMutation(int concurrency)
        {
            await using Harness harness = new();
            FileInfo firstPayload = harness.File(@"first\v1\setup.exe", "first executable");
            harness.Application(@"first\v1\App.manifest", firstPayload.Name);
            FileInfo publisher = harness.Deployment(@"first\A.application", @"v1\App.manifest");
            FileInfo protectedPayload = harness.File(@"second\v1\setup.exe", "second executable");
            harness.Application(@"second\v1\B.manifest");
            FileInfo protectedOwner = harness.Deployment(@"second\v1\B.application", "B.manifest");
            Matcher exclude = new();
            exclude.AddInclude("setup.exe");
            harness.Options = SelectionOptions(harness, null, exclude);
            SigningOperationPlan[] plans =
            {
                new(SigningFile.Capture(publisher), new FileInfo(Path.Combine(harness.Source.FullName, "second", publisher.Name))),
                new(SigningFile.Capture(protectedOwner), protectedOwner)
            };
            Dictionary<string, byte[]> before = Snapshot(harness);

            SigningException failure = await Assert.ThrowsAsync<SigningException>(() =>
                harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, concurrency));

            Assert.Contains(protectedPayload.FullName, failure.Message);
            AssertUnchanged(harness, before);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(4)]
        public async Task ExecuteAsync_WhenSourceAndDestinationDifferOnlyByCaseInInsensitiveDirectory_RejectsOverlap(int concurrency)
        {
            await ProtectedSourceCaseAsync(concurrency, sensitive: false);
        }

        [RequiresCaseSensitiveDirectoryFact]
        public async Task ExecuteAsync_WhenCaseSensitiveDirectoryHasDistinctProtectedDestination_AllowsPublication()
        {
            await ProtectedSourceCaseAsync(1, sensitive: true);
            await ProtectedSourceCaseAsync(4, sensitive: true);
        }

        private static async Task ProtectedSourceCaseAsync(int concurrency, bool sensitive)
        {
            await using Harness harness = new();
            harness.File(@"first\v1\payload.dll", "first bytes");
            harness.Application(@"first\v1\App.manifest", "payload.dll");
            FileInfo publisher = harness.Deployment(@"first\A.application", @"v1\App.manifest");
            DirectoryInfo secondDirectory = new(Path.Combine(harness.Source.FullName, @"second\v1"));
            secondDirectory.Create();
            if (sensitive)
            {
                Assert.True(CaseSensitiveDirectory.TryEnable(secondDirectory));
            }
            FileInfo protectedPayload = harness.File(@"second\v1\PAYLOAD.dll", "second bytes");
            FileInfo protectedApplication = harness.Application(@"second\v1\B.manifest", protectedPayload.Name);
            Matcher exclude = new();
            exclude.AddInclude("payload.dll");
            harness.Options = SelectionOptions(harness, null, exclude);
            FileInfo output = new(Path.Combine(harness.Source.FullName, "second", publisher.Name));
            SigningOperationPlan[] plans =
            {
                new(SigningFile.Capture(publisher), output),
                new(SigningFile.Capture(protectedApplication), protectedApplication)
            };
            Dictionary<string, byte[]> before = Snapshot(harness);
            if (sensitive)
            {
                await harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, concurrency);
                Assert.Equal("second bytes", System.IO.File.ReadAllText(protectedPayload.FullName));
                Assert.Equal("first bytes signed", System.IO.File.ReadAllText(
                    Path.Combine(secondDirectory.FullName, "payload.dll")));
                harness.AssertPayloadHash(protectedApplication, protectedPayload);
            }
            else
            {
                await Assert.ThrowsAsync<SigningException>(() =>
                    harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, concurrency));
                AssertUnchanged(harness, before);
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(4)]
        public async Task ExecuteAsync_WhenOutputIsDifferentHardLinkPath_AtomicReplacementPreservesProtectedSource(int concurrency)
        {
            await using Harness harness = new();
            harness.File(@"first\v1\alias.dll", "first bytes");
            harness.Application(@"first\v1\App.manifest", "alias.dll");
            FileInfo publisher = harness.Deployment(@"first\A.application", @"v1\App.manifest");
            FileInfo protectedPayload = harness.File(@"second\v1\payload.dll", "second bytes");
            FileInfo protectedApplication = harness.Application(@"second\v1\B.manifest", protectedPayload.Name);
            FileInfo alias = new(Path.Combine(protectedPayload.DirectoryName!, "alias.dll"));
            HardLink.Create(alias.FullName, protectedPayload.FullName);
            Matcher exclude = new();
            exclude.AddInclude("payload.dll");
            harness.Options = SelectionOptions(harness, null, exclude);
            SigningOperationPlan[] plans =
            {
                new(SigningFile.Capture(publisher), new FileInfo(Path.Combine(harness.Source.FullName, "second", publisher.Name))),
                new(SigningFile.Capture(protectedApplication), protectedApplication)
            };

            await harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, concurrency);

            Assert.Equal("second bytes", System.IO.File.ReadAllText(protectedPayload.FullName));
            Assert.Equal("first bytes signed", System.IO.File.ReadAllText(alias.FullName));
            harness.AssertPayloadHash(protectedApplication, protectedPayload);
        }
    }
}

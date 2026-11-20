// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Globalization;
using Microsoft.Extensions.FileSystemGlobbing;

namespace Sign.Core.Test
{
    public sealed partial class ClickOnceDeploymentManifestSignerTests
    {
        [Theory]
        [InlineData(0, false)]
        [InlineData(0, true)]
        [InlineData(1, false)]
        [InlineData(1, true)]
        [InlineData(2, false)]
        [InlineData(2, true)]
        [InlineData(3, false)]
        [InlineData(3, true)]
        public async Task ReadOnly_SelectedSource_IsRejectedWithoutPublication(int kind, bool outOfPlace)
        {
            await using Harness harness = new();
            FileInfo payload = harness.File(@"v1\payload.dll.deploy", "payload");
            FileInfo application = harness.Application(@"v1\App.manifest", "payload.dll");
            FileInfo deployment = harness.Deployment("App.application", @"v1\App.manifest", mapped: true);
            FileInfo setup = harness.File("setup.exe", "setup");
            FileInfo source = kind switch { 0 => deployment, 1 => application, 2 => payload, _ => setup };
            source.IsReadOnly = true;
            Dictionary<string, byte[]> before = Snapshot(harness);
            try
            {
                SigningException failure = await Assert.ThrowsAsync<SigningException>(() =>
                    harness.ExecuteAsync(deployment, outOfPlace ? harness.Output(deployment.Name) : deployment));

                Assert.Equal(string.Format(CultureInfo.CurrentCulture, Resources.ClickOnceReadOnlySource,
                    source.FullName), failure.Message);
                Assert.DoesNotContain(kind == 2 ? "payload.dll" : source.Name, harness.Calls);
                AssertUnchanged(harness, before);
                source.Refresh();
                Assert.True(source.IsReadOnly);
                await harness.Coordinator.DisposeAsync();
                AssertStagingDeleted(harness);
            }
            finally
            {
                source.IsReadOnly = false;
                CleanupReadOnlyTestCopies(harness);
            }
        }

        [Theory]
        [InlineData(1, false)]
        [InlineData(4, false)]
        [InlineData(1, true)]
        [InlineData(4, true)]
        public async Task ReadOnly_ExplicitGraphPrerequisite_IsRejectedBeforeSigning(
            int concurrency, bool updateOnly)
        {
            await using Harness harness = new();
            FileInfo payload = harness.File("payload.dll", "payload");
            FileInfo application = harness.Application("App.manifest", payload.Name);
            FileInfo deployment = harness.Deployment("App.application", application.Name);
            SigningOperationPlan[] plans =
            {
                new(SigningFile.Capture(payload), harness.Output(payload.Name)),
                new(SigningFile.Capture(application), harness.Output(application.Name)),
                new(SigningFile.Capture(deployment), harness.Output(deployment.Name))
            };
            payload.IsReadOnly = true;
            Dictionary<string, byte[]> before = Snapshot(harness);
            try
            {
                SigningException failure = await Assert.ThrowsAsync<SigningException>(() =>
                    harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, concurrency,
                        updateOnly ? ClickOnceApplicationSigningPolicy.UpdateManifestOnly :
                            ClickOnceApplicationSigningPolicy.SignPayloads));

                Assert.Equal(string.Format(CultureInfo.CurrentCulture, Resources.ClickOnceReadOnlySource,
                    payload.FullName), failure.Message);
                Assert.Empty(harness.Calls);
                AssertUnchanged(harness, before);
                payload.Refresh();
                Assert.True(payload.IsReadOnly);
                await harness.Coordinator.DisposeAsync();
                AssertStagingDeleted(harness);
            }
            finally
            {
                payload.IsReadOnly = false;
                CleanupReadOnlyTestCopies(harness);
            }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task ReadOnly_NoUpdateManifest_IsRejected(bool applicationOnly, bool outOfPlace)
        {
            await using Harness harness = new();
            FileInfo manifest = applicationOnly
                ? harness.Application("App.manifest", "missing.dll")
                : harness.Deployment("App.vsto", "missing.manifest");
            manifest.IsReadOnly = true;
            Dictionary<string, byte[]> before = Snapshot(harness);
            SigningOperationPlan plan = new(SigningFile.Capture(manifest),
                outOfPlace ? harness.Output(manifest.Name) : manifest);
            try
            {
                SigningException failure = await Assert.ThrowsAsync<SigningException>(() => applicationOnly
                    ? harness.Pipeline.ExecuteApplicationAsync(plan, harness.Options, harness.Coordinator,
                        updateManifest: false)
                    : harness.Pipeline.ExecuteAsync(plan, harness.Options, harness.Coordinator,
                        updateManifest: false));

                Assert.Equal(string.Format(CultureInfo.CurrentCulture, Resources.ClickOnceReadOnlySource,
                    manifest.FullName), failure.Message);
                Assert.Empty(harness.Calls);
                AssertUnchanged(harness, before);
                manifest.Refresh();
                Assert.True(manifest.IsReadOnly);
                await harness.Coordinator.DisposeAsync();
                AssertStagingDeleted(harness);
            }
            finally
            {
                manifest.IsReadOnly = false;
                CleanupReadOnlyTestCopies(harness);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ReadOnly_UpdateOnlyApplication_IsRejected(bool outOfPlace)
        {
            await using Harness harness = new();
            harness.File("payload.dll", "payload");
            FileInfo application = harness.Application("App.manifest", "payload.dll");
            application.IsReadOnly = true;
            Dictionary<string, byte[]> before = Snapshot(harness);
            try
            {
                SigningException failure = await Assert.ThrowsAsync<SigningException>(() =>
                    harness.Pipeline.ExecuteApplicationAsync(new(SigningFile.Capture(application),
                        outOfPlace ? harness.Output(application.Name) : application),
                        harness.Options, harness.Coordinator, ClickOnceApplicationSigningPolicy.UpdateManifestOnly));

                Assert.Equal(string.Format(CultureInfo.CurrentCulture, Resources.ClickOnceReadOnlySource,
                    application.FullName), failure.Message);
                Assert.Empty(harness.Calls);
                AssertUnchanged(harness, before);
                application.Refresh();
                Assert.True(application.IsReadOnly);
                await harness.Coordinator.DisposeAsync();
                AssertStagingDeleted(harness);
            }
            finally
            {
                application.IsReadOnly = false;
                CleanupReadOnlyTestCopies(harness);
            }
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(0, true)]
        [InlineData(1, false)]
        [InlineData(1, true)]
        [InlineData(2, false)]
        [InlineData(2, true)]
        public async Task ReadOnly_UnmodifiedDependencies_AreAllowed(int mode, bool mapped)
        {
            await using Harness harness = new();
            FileInfo payload = harness.File("payload.dll" + (mapped ? ".deploy" : ""), "payload");
            FileInfo data = harness.File("data.txt" + (mapped ? ".deploy" : ""), "data");
            FileInfo setup = harness.File("setup.exe", "setup");
            FileInfo application = harness.Application("App.manifest", "payload.dll", "data.txt");
            FileInfo deployment = harness.Deployment("App.application", application.Name, mapped);
            FileInfo[] readOnly = mode == 0 ? new[] { data } :
                mode == 1 ? new[] { payload, data, setup } : new[] { application, payload, data, setup };
            if (mode == 1)
            {
                Matcher excluded = new();
                excluded.AddInclude("**/payload.dll*");
                excluded.AddInclude("**/setup.exe");
                harness.Options = new(null, null, null, null, harness.Options.FileHashAlgorithm,
                    harness.Options.TimestampHashAlgorithm, harness.Options.TimestampService, null, excluded, true);
            }
            Dictionary<string, byte[]> before = readOnly.ToDictionary(file => file.FullName,
                file => System.IO.File.ReadAllBytes(file.FullName));
            foreach (FileInfo file in readOnly)
            {
                file.IsReadOnly = true;
            }
            try
            {
                harness.BeforeSign = _ =>
                {
                    foreach (DirectoryInfo directory in harness.TemporaryDirectories)
                    {
                        directory.Refresh();
                        if (directory.Exists && directory.FullName != harness.Source.FullName)
                        {
                            foreach (FileInfo copy in directory.EnumerateFiles("*", SearchOption.AllDirectories)
                                .Where(file => readOnly.Any(source => file.Name == source.Name ||
                                    (source.Name.EndsWith(".deploy", StringComparison.Ordinal) &&
                                        file.Name == source.Name[..^7]))))
                            {
                                Assert.True(copy.IsReadOnly, $"Read-only staging copy became writable: {copy.FullName}");
                            }
                        }
                    }
                };
                await harness.ExecuteAsync(deployment, harness.Output(deployment.Name),
                    mode == 2 ? ClickOnceApplicationSigningPolicy.UpdateManifestOnly :
                        ClickOnceApplicationSigningPolicy.SignPayloads);

                foreach (FileInfo file in readOnly)
                {
                    Assert.Equal(before[file.FullName], System.IO.File.ReadAllBytes(file.FullName));
                    file.Refresh();
                    Assert.True(file.IsReadOnly);
                    Assert.DoesNotContain(file.Name, harness.Calls);
                }
                await harness.Coordinator.DisposeAsync();
                AssertStagingDeleted(harness);
            }
            finally
            {
                foreach (FileInfo file in readOnly)
                {
                    file.IsReadOnly = false;
                }
                CleanupReadOnlyTestCopies(harness);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ReadOnly_UnmodifiedCopy_IsCleanedAfterSigningFailure(bool mapped)
        {
            await using Harness harness = new();
            FileInfo data = harness.File("data.txt" + (mapped ? ".deploy" : ""), "data");
            harness.Application("App.manifest", "data.txt");
            FileInfo deployment = harness.Deployment("App.application", "App.manifest", mapped);
            data.IsReadOnly = true;
            Dictionary<string, byte[]> before = Snapshot(harness);
            InvalidOperationException expected = new("Injected signing failure");
            harness.BeforeSign = _ => throw expected;
            try
            {
                InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    harness.ExecuteAsync(deployment, harness.Output(deployment.Name)));

                Assert.Same(expected, actual);
                AssertUnchanged(harness, before);
                data.Refresh();
                Assert.True(data.IsReadOnly);
                await harness.Coordinator.DisposeAsync();
                AssertStagingDeleted(harness);
            }
            finally
            {
                data.IsReadOnly = false;
                CleanupReadOnlyTestCopies(harness);
            }
        }

        [Fact]
        public async Task ReadOnly_ExcludedDependency_IsNotModifiedAndStagingIsDeleted()
        {
            await using Harness harness = new();
            FileInfo payload = harness.File("payload.dll", "payload");
            harness.Application("App.manifest", payload.Name);
            FileInfo deployment = harness.Deployment("App.application", "App.manifest");
            Matcher excluded = new();
            excluded.AddInclude("**/payload.dll");
            harness.Options = new(null, null, null, null, harness.Options.FileHashAlgorithm,
                harness.Options.TimestampHashAlgorithm, harness.Options.TimestampService, null, excluded, true);
            payload.IsReadOnly = true;
            try
            {
                await harness.ExecuteAsync(deployment, harness.Output(deployment.Name));

                Assert.DoesNotContain(payload.Name, harness.Calls);
                Assert.Equal("payload", System.IO.File.ReadAllText(payload.FullName));
                payload.Refresh();
                Assert.True(payload.IsReadOnly);
                await harness.Coordinator.DisposeAsync();
                AssertStagingDeleted(harness);
            }
            finally
            {
                payload.IsReadOnly = false;
                CleanupReadOnlyTestCopies(harness);
            }
        }

        private static void AssertStagingDeleted(Harness harness)
        {
            foreach (DirectoryInfo directory in harness.TemporaryDirectories.Where(directory =>
                directory.FullName != harness.Source.FullName))
            {
                directory.Refresh();
                Assert.False(directory.Exists, $"Staging directory leaked: {directory.FullName}");
            }
        }

        private static void CleanupReadOnlyTestCopies(Harness harness)
        {
            foreach (DirectoryInfo directory in harness.TemporaryDirectories.Where(directory =>
                directory.FullName != harness.Source.FullName))
            {
                directory.Refresh();
                if (directory.Exists)
                {
                    foreach (FileInfo file in directory.EnumerateFiles("*", SearchOption.AllDirectories))
                    {
                        file.IsReadOnly = false;
                    }
                    directory.Delete(recursive: true);
                }
            }
        }
    }
}

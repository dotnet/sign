// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using NSubstitute;
using Microsoft.Build.Tasks.Deployment.ManifestUtilities;

namespace Sign.Core.Test
{
    public sealed class ResolvedClickOnceApplicationTests
    {
        private const string ApplicationManifestFileName = "App.exe.manifest";
        private const string PayloadFileName = "payload.dll";

        [Fact]
        public void Constructor_WithValidArguments_PreservesValuesAndSnapshotsPayloads()
        {
            FileInfo source = CreateSource();
            IApplicationManifest manifest = Substitute.For<IApplicationManifest>();
            ResolvedClickOncePayload payload = new(
                new FileInfo(PayloadFileName),
                new FileReference()
                {
                    TargetPath = PayloadFileName
                });
            List<ResolvedClickOncePayload> payloads = new() { payload };

            ResolvedClickOnceApplication application = new(
                source,
                manifest,
                payloads);

            payloads.Clear();

            Assert.Same(source, application.Source);
            Assert.Same(manifest, application.Manifest);
            Assert.Same(payload, Assert.Single(application.Payloads));
        }

        [Fact]
        public void Constructor_WithNullSource_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(
                () => new ResolvedClickOnceApplication(
                    source: null!,
                    Substitute.For<IApplicationManifest>(),
                    payloads: Array.Empty<ResolvedClickOncePayload>()));
        }

        [Fact]
        public void Constructor_WithNullManifest_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(
                () => new ResolvedClickOnceApplication(
                    CreateSource(),
                    manifest: null!,
                    payloads: Array.Empty<ResolvedClickOncePayload>()));
        }

        [Fact]
        public void Constructor_WithNullPayloads_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(
                () => new ResolvedClickOnceApplication(
                    CreateSource(),
                    Substitute.For<IApplicationManifest>(),
                    payloads: null!));
        }

        private static FileInfo CreateSource()
        {
            return new FileInfo(ApplicationManifestFileName);
        }
    }
}

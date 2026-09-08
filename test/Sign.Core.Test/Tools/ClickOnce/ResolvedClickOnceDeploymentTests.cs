// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Build.Tasks.Deployment.ManifestUtilities;
using NSubstitute;

namespace Sign.Core.Test
{
    public sealed class ResolvedClickOnceDeploymentTests
    {
        private const string DeploymentManifestFileName = "App.application";

        [Fact]
        public void Constructor_WithValidArguments_PreservesValues()
        {
            FileInfo source = CreateSource();
            IDeployManifest manifest = Substitute.For<IDeployManifest>();
            AssemblyReference applicationManifestReference = new();
            manifest.EntryPoint.Returns(applicationManifestReference);

            ResolvedClickOnceDeployment deployment = new(
                source,
                manifest,
                applicationManifestReference);

            Assert.Same(source, deployment.Source);
            Assert.Same(manifest, deployment.Manifest);
            Assert.Same(
                applicationManifestReference,
                deployment.ApplicationManifestReference);
        }

        [Fact]
        public void Constructor_WithNullSource_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(
                () => new ResolvedClickOnceDeployment(
                    source: null!,
                    Substitute.For<IDeployManifest>(),
                    new AssemblyReference()));
        }

        [Fact]
        public void Constructor_WithNullManifest_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(
                () => new ResolvedClickOnceDeployment(
                    CreateSource(),
                    manifest: null!,
                    new AssemblyReference()));
        }

        [Fact]
        public void Constructor_WithNullApplicationManifestReference_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(
                () => new ResolvedClickOnceDeployment(
                    CreateSource(),
                    Substitute.For<IDeployManifest>(),
                    applicationManifestReference: null!));
        }

        [Fact]
        public void Constructor_WithReferenceOtherThanManifestEntryPoint_ThrowsArgumentException()
        {
            IDeployManifest manifest = Substitute.For<IDeployManifest>();
            manifest.EntryPoint.Returns(new AssemblyReference());

            Assert.Throws<ArgumentException>(
                () => new ResolvedClickOnceDeployment(
                    CreateSource(),
                    manifest,
                    new AssemblyReference()));
        }

        private static FileInfo CreateSource()
        {
            return new FileInfo(DeploymentManifestFileName);
        }
    }
}

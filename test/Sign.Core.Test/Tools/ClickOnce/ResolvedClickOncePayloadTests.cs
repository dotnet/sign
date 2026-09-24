// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Build.Tasks.Deployment.ManifestUtilities;

namespace Sign.Core.Test
{
    public sealed class ResolvedClickOncePayloadTests
    {
        private const string DeploySuffix = ".deploy";
        private const string PayloadFileName = "payload.dll";

        [Fact]
        public void Constructor_WithValidArguments_PreservesValues()
        {
            FileInfo source = new(fileName: $"{PayloadFileName}{DeploySuffix}");
            FileReference manifestReference = new()
            {
                TargetPath = PayloadFileName
            };

            ResolvedClickOncePayload payload = new(
                source,
                manifestReference);

            Assert.Same(source, payload.Source);
            Assert.Equal(PayloadFileName, payload.TargetPath);
            Assert.Same(manifestReference, payload.Reference);
            Assert.True(payload.IsFileExtensionMapped);
        }

        [Fact]
        public void TargetPath_WhenReferenceTargetPathChanges_ReturnsResolvedValue()
        {
            FileReference manifestReference = new()
            {
                TargetPath = PayloadFileName
            };
            ResolvedClickOncePayload payload = new(
                new FileInfo(PayloadFileName),
                manifestReference);

            manifestReference.TargetPath = "renamed.dll";

            Assert.Equal(PayloadFileName, payload.TargetPath);
        }

        [Fact]
        public void Constructor_WithoutFileExtensionMapping_ClearsIsFileExtensionMapped()
        {
            ResolvedClickOncePayload payload = new(
                new FileInfo(PayloadFileName),
                new FileReference()
                {
                    TargetPath = PayloadFileName
                });

            Assert.False(payload.IsFileExtensionMapped);
        }

        [Fact]
        public void Constructor_WhenTargetPathEndsInDeployAndSourceMatches_DoesNotSetIsFileExtensionMapped()
        {
            const string TargetPath = $"{PayloadFileName}{DeploySuffix}";
            ResolvedClickOncePayload payload = new(
                new FileInfo(TargetPath),
                new FileReference()
                {
                    TargetPath = TargetPath
                });

            Assert.False(payload.IsFileExtensionMapped);
        }

        [Fact]
        public void Constructor_WhenTargetPathEndsInDeployAndSourceHasAdditionalDeploySuffix_SetsIsFileExtensionMapped()
        {
            const string TargetPath = $"{PayloadFileName}{DeploySuffix}";
            ResolvedClickOncePayload payload = new(
                new FileInfo($"{TargetPath}{DeploySuffix}"),
                new FileReference()
                {
                    TargetPath = TargetPath
                });

            Assert.True(payload.IsFileExtensionMapped);
        }

        [Fact]
        public void Constructor_WithMismatchedSourceFileName_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(
                () => new ResolvedClickOncePayload(
                    new FileInfo("other.dll"),
                    new FileReference()
                    {
                        TargetPath = PayloadFileName
                    }));
        }

        [Fact]
        public void Constructor_WithNullSource_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(
                () => new ResolvedClickOncePayload(
                    source: null!,
                    new FileReference()
                    {
                        TargetPath = PayloadFileName
                    }));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        public void Constructor_WithInvalidReferenceTargetPath_ThrowsArgumentException(
            string? targetPath)
        {
            Assert.ThrowsAny<ArgumentException>(
                () => new ResolvedClickOncePayload(
                    new FileInfo(PayloadFileName),
                    new FileReference()
                    {
                        TargetPath = targetPath!
                    }));
        }

        [Fact]
        public void Constructor_WithNullReference_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(
                () => new ResolvedClickOncePayload(
                    new FileInfo(PayloadFileName),
                    reference: null!));
        }
    }
}

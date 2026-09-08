// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Build.Tasks.Deployment.ManifestUtilities;

namespace Sign.Core.Test
{
    public sealed class ClickOncePublishLayoutResolutionExceptionTests
    {
        private const string ResolutionFailedMessage = "Resolution failed.";
        private const string WarningMessageName = "GenerateManifest.ResolveFailedInReadWriteMode";
        private const string WarningTargetPath = "warning.txt";

        [Fact]
        public void Constructor_WithValues_PreservesValuesAndSnapshotsDiagnostics()
        {
            ClickOnceManifestDiagnostic diagnostic = CreateDiagnostic();
            List<ClickOnceManifestDiagnostic> diagnostics = new() { diagnostic };
            InvalidOperationException innerException = new();

            ClickOncePublishLayoutResolutionException exception = new(
                ResolutionFailedMessage,
                diagnostics,
                innerException);

            diagnostics.Clear();

            Assert.Equal(ResolutionFailedMessage, exception.Message);
            Assert.Same(diagnostic, Assert.Single(exception.Diagnostics));
            Assert.Same(innerException, exception.InnerException);
        }

        [Fact]
        public void Constructor_WithoutDiagnostics_DefaultsToEmptyDiagnostics()
        {
            ClickOncePublishLayoutResolutionException exception = new(
                message: ResolutionFailedMessage);

            Assert.Empty(exception.Diagnostics);
        }

        [Fact]
        public void Constructor_WithWhitespaceMessage_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(
                () => new ClickOncePublishLayoutResolutionException(message: " "));
        }

        private static ClickOnceManifestDiagnostic CreateDiagnostic()
        {
            return new ClickOnceManifestDiagnostic(
                WarningMessageName,
                WarningTargetPath,
                OutputMessageType.Warning);
        }
    }
}

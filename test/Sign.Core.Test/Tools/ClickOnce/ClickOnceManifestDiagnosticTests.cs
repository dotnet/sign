// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Build.Tasks.Deployment.ManifestUtilities;

namespace Sign.Core.Test
{
    public sealed class ClickOnceManifestDiagnosticTests
    {
        private const string WarningMessageName = "GenerateManifest.ResolveFailedInReadWriteMode";
        private const string WarningTargetPath = "warning.txt";

        [Fact]
        public void Constructor_WithValidMessage_PreservesValues()
        {
            ClickOnceManifestDiagnostic diagnostic = new(
                WarningMessageName,
                WarningTargetPath,
                OutputMessageType.Warning);

            Assert.Equal(WarningMessageName, diagnostic.Name);
            Assert.Equal(WarningTargetPath, diagnostic.Text);
            Assert.Equal(OutputMessageType.Warning, diagnostic.Type);
        }

        [Fact]
        public void Constructor_WithNullMessage_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(
                () => new ClickOnceManifestDiagnostic(message: null!));
        }
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Build.Tasks.Deployment.ManifestUtilities;

namespace Sign.Core
{
    internal sealed class ClickOnceManifestDiagnostic
    {
        internal ClickOnceManifestDiagnostic(OutputMessage message)
        {
            ArgumentNullException.ThrowIfNull(message, nameof(message));

            Name = message.Name;
            Text = message.Text;
            Type = message.Type;
        }

        internal ClickOnceManifestDiagnostic(
            string name,
            string text,
            OutputMessageType type)
        {
            ArgumentNullException.ThrowIfNull(name, nameof(name));
            ArgumentNullException.ThrowIfNull(text, nameof(text));

            Name = name;
            Text = text;
            Type = type;
        }

        internal string Name { get; }
        internal string Text { get; }
        internal OutputMessageType Type { get; }
    }
}

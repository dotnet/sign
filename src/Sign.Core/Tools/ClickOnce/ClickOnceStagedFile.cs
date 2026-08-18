// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Build.Tasks.Deployment.ManifestUtilities;

namespace Sign.Core
{
    internal sealed class ClickOnceStagedFile
    {
        internal ClickOnceStagedFile(
            ClickOnceFileGraphEntry graphEntry,
            FileInfo file,
            FileInfo? manifestUpdateFile,
            bool isUpdateInputOnly)
        {
            ArgumentNullException.ThrowIfNull(graphEntry, nameof(graphEntry));
            ArgumentNullException.ThrowIfNull(file, nameof(file));

            GraphEntry = graphEntry;
            File = file;
            ManifestUpdateFile = manifestUpdateFile;
            IsUpdateInputOnly = isUpdateInputOnly;
        }

        internal ClickOnceFileGraphEntry GraphEntry { get; }
        internal FileInfo Source => GraphEntry.Source;
        internal FileInfo File { get; }
        internal FileInfo? ManifestUpdateFile { get; }
        internal string TargetPath => GraphEntry.TargetPath;
        internal ClickOnceFileGraphEntryKind Kind => GraphEntry.Kind;
        internal BaseReference? ManifestReference =>
            GraphEntry.ManifestReference;
        internal bool IsUpdateInputOnly { get; }
    }
}

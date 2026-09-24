// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal sealed class ResolvedClickOnceAdjacentExecutable
    {
        internal const string LauncherFileName = "Launcher.exe";
        internal const string SetupFileName = "setup.exe";

        internal ResolvedClickOnceAdjacentExecutable(
            FileInfo source,
            ClickOnceAdjacentExecutableKind kind)
        {
            ArgumentNullException.ThrowIfNull(source, nameof(source));

            string expectedFileName = kind switch
            {
                ClickOnceAdjacentExecutableKind.Setup => SetupFileName,
                ClickOnceAdjacentExecutableKind.Launcher => LauncherFileName,
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };

            if (!string.Equals(
                source.Name,
                expectedFileName,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "The source file name does not match the adjacent executable kind.",
                    nameof(source));
            }

            Source = source;
            Kind = kind;
        }

        internal FileInfo Source { get; }
        internal string TargetPath => Source.Name;
        internal ClickOnceAdjacentExecutableKind Kind { get; }
    }
}

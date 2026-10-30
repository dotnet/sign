// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal enum SipKind
    {
        None,
        Appx
    }

    internal static class SipExtensionFactory
    {
        public static SipKind GetSipKind(ReadOnlySpan<char> filePath)
        {
            ReadOnlySpan<char> extension = Path.GetExtension(filePath);

            if (extension.Equals(".appx", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".eappx", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".appxbundle", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".eappxbundle", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".msix", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".emsix", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".msixbundle", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".emsixbundle", StringComparison.OrdinalIgnoreCase))
            {
                return SipKind.Appx;
            }

            return SipKind.None;
        }
    }
}

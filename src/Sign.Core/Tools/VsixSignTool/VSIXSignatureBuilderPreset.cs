// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    /// <summary>
    /// The preset for VSIX files.
    /// </summary>
    internal sealed class VSIXSignatureBuilderPreset : ISignatureBuilderPreset
    {
        IEnumerable<OpcPart> ISignatureBuilderPreset.GetPartsForSigning(OpcPackage package)
        {
            var existingSignatures = package.GetSignatures().ToList();
            foreach (var part in package.GetParts())
            {
                if (existingSignatures.All(existing => Uri.Compare(part.Uri, existing.Part?.Uri, UriComponents.Path, UriFormat.Unescaped, StringComparison.Ordinal) != 0))
                {
                    yield return part;
                }
            }
        }
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    /// <summary>
    /// Defines an interface for package signing presets.
    /// </summary>
    internal interface ISignatureBuilderPreset
    {
        /// <summary>
        /// Returns a collection of parts that should be enqueued for signing.
        /// </summary>
        /// <param name="package">A package to list the parts from.</param>
        /// <returns>A collection of parts.</returns>
        IEnumerable<OpcPart> GetPartsForSigning(OpcPackage package);
    }
}

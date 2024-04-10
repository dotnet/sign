// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    /// <summary>
    /// Indicates a signing algorithm.
    /// </summary>
    internal enum SigningAlgorithm
    {
        /// <summary>
        /// The signing algorithm is unknown.
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// The signing algorithm is RSA.
        /// </summary>
        RSA,
    }
}
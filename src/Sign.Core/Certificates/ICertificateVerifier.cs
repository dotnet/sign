// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography.X509Certificates;

namespace Sign.Core
{
    internal interface ICertificateVerifier
    {
        void Verify(X509Certificate2 certificate);
    }
}
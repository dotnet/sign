// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Azure.Core;

namespace Sign.Core
{
    internal interface IKeyVaultService
    {
        Task<X509Certificate2> GetCertificateAsync();
        Task<RSA> GetRsaAsync();
        void Initialize(Uri keyVaultUrl, TokenCredential tokenCredential, string certificateName);
    }
}
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    /// <summary>
    /// Interface used to define <see cref="CertificateManagerService"/>'s initialize method for accessing
    /// Windows Certificate Manager resources.
    /// </summary>
    internal interface ICertificateStoreService : ICertificateService
    {
        void Initialize(string sha1Thumbprint, string? cryptoServiceProvider, string? privateKeyContainer, string? privateMachineKeyContainer);
    }
}
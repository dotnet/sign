// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.DependencyInjection;
using Sign.Core;

namespace Sign.SignatureProviders.TrustedSigning
{
    internal sealed class TrustedSigningServiceProvider : ISignatureProvider
    {
        public ISignatureAlgorithmProvider GetSignatureAlgorithmProvider(IServiceProvider serviceProvider)
        {
            ArgumentNullException.ThrowIfNull(serviceProvider, nameof(serviceProvider));

            return serviceProvider.GetRequiredService<TrustedSigningService>();
        }

        public ICertificateProvider GetCertificateProvider(IServiceProvider serviceProvider)
        {
            ArgumentNullException.ThrowIfNull(serviceProvider, nameof(serviceProvider));

            return serviceProvider.GetRequiredService<TrustedSigningService>();
        }
    }
}

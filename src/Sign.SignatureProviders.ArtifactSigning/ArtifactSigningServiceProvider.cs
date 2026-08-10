// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.DependencyInjection;
using Sign.Core;

namespace Sign.SignatureProviders.ArtifactSigning
{
    internal sealed class ArtifactSigningServiceProvider : ISignatureProvider
    {
        private ICertificateProvider? _certificateProvider;

        internal ICertificateProvider CertificateProvider =>
            _certificateProvider ?? throw new InvalidOperationException("The certificate provider has not been created.");

        public ISignatureAlgorithmProvider GetSignatureAlgorithmProvider(IServiceProvider serviceProvider)
        {
            ArgumentNullException.ThrowIfNull(serviceProvider, nameof(serviceProvider));

            return serviceProvider.GetRequiredService<ArtifactSigningService>();
        }

        public ICertificateProvider GetCertificateProvider(IServiceProvider serviceProvider)
        {
            ArgumentNullException.ThrowIfNull(serviceProvider, nameof(serviceProvider));

            _certificateProvider = serviceProvider.GetRequiredService<ArtifactSigningService>();

            return _certificateProvider;
        }
    }
}

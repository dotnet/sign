// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using NuGet.Common;
using NuGet.Packaging.Signing;

namespace Sign.Core
{
    internal sealed class NuGetSignatureProvider : NuGet.Packaging.Signing.ISignatureProvider
    {
        // Occurs when SignedCms.ComputeSignature cannot read the certificate private key
        // "Invalid provider type specified." (INVALID_PROVIDER_TYPE)
        private const int INVALID_PROVIDER_TYPE_HRESULT = unchecked((int)0x80090014);

        private readonly RSA _rsa;
        private readonly ITimestampProvider _timestampProvider;

        public NuGetSignatureProvider(RSA rsa, ITimestampProvider timestampProvider)
        {
            ArgumentNullException.ThrowIfNull(rsa, nameof(rsa));
            ArgumentNullException.ThrowIfNull(timestampProvider, nameof(timestampProvider));

            _rsa = rsa;
            _timestampProvider = timestampProvider;
        }

        public Task<PrimarySignature> CreatePrimarySignatureAsync(
            SignPackageRequest request,
            SignatureContent signatureContent,
            ILogger logger,
            CancellationToken token)
        {
            if (request is AuthorSignPackageRequest authorSignPackageRequest)
            {
                return CreateAuthorSignatureAsync(authorSignPackageRequest, signatureContent, logger, token);
            }

            throw new NotSupportedException($"Unsupported {nameof(SignPackageRequest)} type: {request.GetType().Name}");
        }

        public Task<PrimarySignature> CreateRepositoryCountersignatureAsync(
            RepositorySignPackageRequest request,
            PrimarySignature primarySignature,
            ILogger logger,
            CancellationToken token)
        {
            throw new NotSupportedException($"Unsupported {nameof(SignPackageRequest)} type: {request.GetType().Name}");
        }

        private async Task<PrimarySignature> CreateAuthorSignatureAsync(
            AuthorSignPackageRequest request,
            SignatureContent signatureContent,
            ILogger logger,
            CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(request, nameof(request));
            ArgumentNullException.ThrowIfNull(signatureContent, nameof(signatureContent));
            ArgumentNullException.ThrowIfNull(logger, nameof(logger));

            logger.LogInformation($"{nameof(CreateAuthorSignatureAsync)}: Creating primary signature");
            PrimarySignature authorSignature = CreatePrimarySignature(request, signatureContent, logger);
            logger.LogInformation($"{nameof(CreateAuthorSignatureAsync)}: Primary signature completed");

            logger.LogInformation($"{nameof(CreateAuthorSignatureAsync)}: Timestamping primary signature");
            PrimarySignature timestampedAuthorSignature = await TimestampPrimarySignatureAsync(request, logger, authorSignature, token);
            logger.LogInformation($"{nameof(CreateAuthorSignatureAsync)}: Timestamping completed");

            return timestampedAuthorSignature;
        }

        private PrimarySignature CreatePrimarySignature(AuthorSignPackageRequest request, SignatureContent signatureContent, ILogger logger)
        {
            logger.LogInformation($"{nameof(CreateAuthorSignatureAsync)}: Retrieving certificate chain");
            const string PropertyName = "Chain";

            PropertyInfo? property = typeof(SignPackageRequest)
                .GetProperty(PropertyName, BindingFlags.Instance | BindingFlags.NonPublic);

            if (property is null)
            {
                throw new MissingMemberException(nameof(SignPackageRequest), PropertyName);
            }

            MethodInfo? getter = property.GetGetMethod(nonPublic: true);

            if (getter is null)
            {
                throw new MissingMemberException(nameof(SignPackageRequest), PropertyName);
            }

            var certificates = (IReadOnlyList<X509Certificate2>?)getter.Invoke(request, parameters: null);
            logger.LogInformation($"{nameof(CreateAuthorSignatureAsync)}: Retrieved certificate chain");


            logger.LogInformation($"{nameof(CreateAuthorSignatureAsync)}: Computing signature");
            CmsSigner cmsSigner = CreateCmsSigner(request, certificates!);
            ContentInfo contentInfo = new(signatureContent.GetBytes());
            SignedCms signedCms = new(contentInfo);

            try
            {
                signedCms.ComputeSignature(cmsSigner, silent: false); // silent is false to ensure PIN prompts appear if CNG/CAPI requires it
            }
            catch (CryptographicException ex) when (ex.HResult == INVALID_PROVIDER_TYPE_HRESULT)
            {
                StringBuilder stringBuilder = new();
                stringBuilder.AppendLine("Invalid _rsa type");
                stringBuilder.AppendLine(CertificateUtility.X509Certificate2ToString(request.Certificate, NuGet.Common.HashAlgorithmName.SHA256));

                logger.LogError($"{nameof(CreateAuthorSignatureAsync)}: Cannot read private key");

                throw new SignatureException(NuGetLogCode.NU3001, stringBuilder.ToString());
            }

            return PrimarySignature.Load(signedCms);
        }

        private CmsSigner CreateCmsSigner(SignPackageRequest request, IReadOnlyList<X509Certificate2> chain)
        {
            // Subject Key Identifier (SKI) is smaller and less prone to accidental matching than issuer and serial
            // number.  However, to ensure cross-platform verification, SKI should only be used if the certificate
            // has the SKI extension attribute.
            CmsSigner signer;

            if (request.Certificate.Extensions[Oids.SubjectKeyIdentifier] == null)
            {
                signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, request.Certificate, _rsa);
            }
            else
            {
                signer = new CmsSigner(SubjectIdentifierType.SubjectKeyIdentifier, request.Certificate, _rsa);
            }

            foreach (X509Certificate2 certificate in chain)
            {
                signer.Certificates.Add(certificate);
            }

            CryptographicAttributeObjectCollection attributes;

            if (request.SignatureType == SignatureType.Repository)
            {
                attributes = SigningUtility.CreateSignedAttributes((RepositorySignPackageRequest)request, chain);
            }
            else
            {
                attributes = SigningUtility.CreateSignedAttributes(request, chain);
            }

            foreach (CryptographicAttributeObject attribute in attributes)
            {
                signer.SignedAttributes.Add(attribute);
            }

            // We built the chain ourselves and added certificates.
            // Passing any other value here would trigger another chain build
            // and possibly add duplicate certs to the collection.
            signer.IncludeOption = X509IncludeOption.None;
            signer.DigestAlgorithm = request.SignatureHashAlgorithm.ConvertToOid();

            return signer;
        }

        private Task<PrimarySignature> TimestampPrimarySignatureAsync(
            SignPackageRequest request,
            ILogger logger,
            PrimarySignature signature,
            CancellationToken token)
        {
            byte[] signatureValue = signature.GetSignatureValue();
            byte[] messageHash = request.TimestampHashAlgorithm.ComputeHash(signatureValue);

            TimestampRequest timestampRequest = new(
                signingSpecifications: SigningSpecifications.V1,
                hashedMessage: messageHash,
                hashAlgorithm: request.TimestampHashAlgorithm,
                target: SignaturePlacement.PrimarySignature);

            return _timestampProvider.TimestampSignatureAsync(signature, timestampRequest, logger, token);
        }
    }
}

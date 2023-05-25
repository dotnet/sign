#pragma warning disable IDE0073
// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Formats.Asn1;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Http;

namespace Sign.Core.Test
{
    // https://tools.ietf.org/html/rfc3161
    internal sealed class TimestampService : HttpResponder, IDisposable
    {
        private const string RequestContentType = "application/timestamp-query";
        private const string ResponseContentType = "application/timestamp-response";

        private readonly HashSet<BigInteger> _serialNumbers;
        private BigInteger _nextSerialNumber;
        private IDisposable? _disposable;

        /// <summary>
        /// Gets this certificate authority's certificate.
        /// </summary>
        internal X509Certificate2 Certificate { get; }

        /// <summary>
        /// Gets the base URI specific to this HTTP responder.
        /// </summary>
        public override Uri Url { get; }

        /// <summary>
        /// Gets the issuing certificate authority.
        /// </summary>
        internal CertificateAuthority CertificateAuthority { get; }

        private TimestampService(
            CertificateAuthority certificateAuthority,
            X509Certificate2 certificate,
            Uri uri)
        {
            CertificateAuthority = certificateAuthority;
            Certificate = certificate;
            Url = uri;
            _serialNumbers = new HashSet<BigInteger>();
            _nextSerialNumber = BigInteger.One;
        }

        public void Dispose()
        {
            _disposable?.Dispose();
        }

        internal static TimestampService Create(
            CertificateAuthority certificateAuthority,
            ITestServer server,
            DateTimeOffset? notAfter = null)
        {
            ArgumentNullException.ThrowIfNull(certificateAuthority, nameof(certificateAuthority));
            ArgumentNullException.ThrowIfNull(server, nameof(server));

            using (RSA rsa = RSA.Create(keySizeInBits: 3072))
            {
                CertificateRequest request = new("CN=timestamp.test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

                request.CertificateExtensions.Add(
                    new X509BasicConstraintsExtension(
                        certificateAuthority: false,
                        hasPathLengthConstraint: false,
                        pathLengthConstraint: 0,
                        critical: true));

                request.CertificateExtensions.Add(
                    new X509KeyUsageExtension(
                        X509KeyUsageFlags.DigitalSignature,
                        critical: true));

                request.CertificateExtensions.Add(
                    new X509EnhancedKeyUsageExtension(
                        new OidCollection()
                        {
                            Oids.TimeStampingEku
                        },
                        critical: true));

                X509Certificate2 certificateWithPrivateKey;

                using (X509Certificate2 certificate = certificateAuthority.Create(request, notAfter))
                {
                    certificateWithPrivateKey = certificate.CopyWithPrivateKey(rsa);
                }

                string UriPrefix = server.Url.AbsoluteUri;

                Uri url = new($"{UriPrefix}timestamp/{certificateWithPrivateKey.Thumbprint}");

                TimestampService timestampService = new(certificateAuthority, certificateWithPrivateKey, url);

                timestampService._disposable = server.RegisterResponder(timestampService);

                return timestampService;
            }
        }

        public override Task RespondAsync(HttpContext context)
        {
            if (!string.Equals(context.Request.ContentType, RequestContentType, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = 400;

                return Task.CompletedTask;
            }

            byte[] bytes = ReadRequestBody(context.Request);
            if (!Rfc3161TimestampRequest.TryDecode(bytes, out Rfc3161TimestampRequest? request, out int _))
            {
                context.Response.StatusCode = 400;

                return Task.CompletedTask;
            }

            ReadOnlyMemory<byte> response;

            if (request.HashAlgorithmId.IsEqualTo(Oids.Sha1))
            {
                response = CreateResponse(PkiStatus.Rejection, signedCms: null);
            }
            else
            {
                ReadOnlyMemory<byte> tstInfo = CreateTstInfo(
                    request.HashAlgorithmId,
                    request.GetMessageHash(),
                    _nextSerialNumber,
                    request.GetNonce());

                _serialNumbers.Add(_nextSerialNumber);

                ++_nextSerialNumber;

                SignedCms timestamp = GenerateTimestamp(request!, tstInfo);
                response = CreateResponse(PkiStatus.Granted, timestamp);
            }

            context.Response.ContentType = ResponseContentType;
            context.Response.StatusCode = 200;

            WriteResponseBody(context.Response, response);

            return Task.CompletedTask;
        }

        private SignedCms GenerateTimestamp(Rfc3161TimestampRequest request, ReadOnlyMemory<byte> tstInfo)
        {
            ArgumentNullException.ThrowIfNull(request, nameof(request));

            ContentInfo contentInfo = new(Oids.TSTInfoContentType, tstInfo.ToArray());
            SignedCms signedCms = new(contentInfo);
            CmsSigner signer = new(SubjectIdentifierType.SubjectKeyIdentifier, Certificate);

            signer.IncludeOption = request.RequestSignerCertificate ? X509IncludeOption.EndCertOnly : X509IncludeOption.None;
            signer.DigestAlgorithm = Oids.Sha256;

            CryptographicAttributeObject signingCertificateV2 = AttributeUtility.CreateSigningCertificateV2Attribute(Certificate, HashAlgorithmName.SHA256);

            signer.SignedAttributes.Add(signingCertificateV2);

            signedCms.ComputeSignature(signer, silent: true);

            return signedCms;
        }

        private static ReadOnlyMemory<byte> CreateTstInfo(
            Oid hashAlgorithmId,
            ReadOnlyMemory<byte> messageHash,
            BigInteger serialNumber,
            ReadOnlyMemory<byte>? nonce)
        {
            AsnWriter writer = new(AsnEncodingRules.DER);

            using (writer.PushSequence())
            {
                writer.WriteInteger(1);  // version
                writer.WriteObjectIdentifier(Oids.TestCertPolicyOne.Value!); // policy

                using (writer.PushSequence()) // messageImprint
                {
                    AlgorithmIdentifier algorithmIdentifier = new(hashAlgorithmId);

                    writer.WriteEncodedValue(algorithmIdentifier.Encode().Span);
                    writer.WriteOctetString(messageHash.Span);
                }

                writer.WriteInteger(serialNumber); // serialNumber
                writer.WriteGeneralizedTime(DateTimeOffset.Now, omitFractionalSeconds: true); // genTime

                using (writer.PushSequence()) // accuracy
                {
                    writer.WriteInteger(1); // seconds
                }

                writer.WriteBoolean(false); // ordering

                if (nonce is not null)
                {
                    writer.WriteInteger(nonce.Value.Span);
                }
            }

            return writer.Encode();
        }

        // See https://datatracker.ietf.org/doc/html/rfc3161#section-2.4.2
        private static ReadOnlyMemory<byte> CreateResponse(PkiStatus pkiStatus, SignedCms? signedCms)
        {
            AsnWriter writer = new(AsnEncodingRules.DER);

            using (writer.PushSequence()) // TimeStampResp
            {
                using (writer.PushSequence()) // PKIStatusInfo
                {
                    writer.WriteInteger((long)pkiStatus);
                }

                if (signedCms is not null)
                {
                    byte[] encodedCms = signedCms.Encode();

                    writer.WriteEncodedValue(encodedCms);
                }
            }

            return writer.Encode();
        }

        private enum PkiStatus
        {
            Granted = 0,
            GrantedWithMods = 1,
            Rejection = 2,
            Waiting = 3,
            RevocationWarning = 4,
            RevocationNotification = 5
        }

        private enum PkiFailureInfo
        {
            BadAlg = 0,
            BadRequest = 2,
            BadDataFormat = 5,
            timeNotAvailable = 14,
            unacceptedPolicy = 15,
            unacceptedExtension = 16,
            addInfoNotAvailable = 17,
            systemFailure = 25
        }
    }
}
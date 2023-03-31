// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Diagnostics;
using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Web;
using Microsoft.AspNetCore.Http;

namespace Sign.Core.Test
{
    internal sealed class OcspResponder : HttpResponder
    {
        private readonly CertificateAuthority _certificateAuthority;
        private readonly TimeSpan? _responseDelay;

        public override Uri Url { get; }

        internal OcspResponder(
            CertificateAuthority certificateAuthority,
            TimeSpan? responseDelay = null)
        {
            ArgumentNullException.ThrowIfNull(certificateAuthority, nameof(certificateAuthority));

            _certificateAuthority = certificateAuthority;
            _responseDelay = responseDelay;

            if (certificateAuthority.OcspUri is null)
            {
                throw new ArgumentException(message: null, nameof(certificateAuthority));
            }

            Url = new Uri(certificateAuthority.OcspUri, UriKind.Absolute);
        }

        public override async Task RespondAsync(HttpContext context)
        {
            if (_responseDelay.HasValue)
            {
                Trace.WriteLine($"Delaying response by {_responseDelay.Value}.");

                await Task.Delay(_responseDelay.Value);
            }

            byte[] requestBytes;

            try
            {
                if (context.Request.Method == "GET")
                {
                    string base64 = HttpUtility.UrlDecode(context.Request.Path.Value[(context.Request.PathBase.Value.Length + 1)..]);
                    requestBytes = Convert.FromBase64String(base64);
                }
                else if (context.Request.Method == "POST" && context.Request.ContentType == "application/ocsp-request")
                {
                    using (Stream stream = context.Request.Body)
                    {
                        requestBytes = new byte[context.Request.ContentLength!.Value];
                        int read = stream.Read(requestBytes, 0, requestBytes.Length);
                        Debug.Assert(read == requestBytes.Length);
                    }
                }
                else
                {
                    return;
                }
            }
            catch (Exception e)
            {
                Trace.WriteLine($"Failed to get OCSP request bytes ({context.Request.Path}) - {e}");

                return;
            }

            ReadOnlyMemory<byte> certId;
            ReadOnlyMemory<byte> nonce;
            try
            {
                DecodeOcspRequest(requestBytes, out certId, out nonce);
            }
            catch (Exception e)
            {
                Trace.WriteLine($"OcspRequest Decode failed ({context.Request.Path}) - {e}");
                context.Response.StatusCode = 400;
                return;
            }

            byte[] ocspResponse = _certificateAuthority.BuildOcspResponse(certId, nonce);

            if (_responseDelay.HasValue)
            {
                Trace.WriteLine($"Delaying response by {_responseDelay.Value}.");

                await Task.Delay(_responseDelay.Value);
            }

            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/ocsp-response";
            await context.Response.Body.WriteAsync(ocspResponse);

            if (_certificateAuthority.HasOcspDelegation)
            {
                Trace.WriteLine($"[OCSP]  Responded with {ocspResponse.Length}-byte certificate status from {_certificateAuthority.SubjectName} delegated to {_certificateAuthority.OcspResponderSubjectName}");
            }
            else
            {
                Trace.WriteLine($"[OCSP]  Responded with {ocspResponse.Length}-byte certificate status from {_certificateAuthority.SubjectName}");
            }
        }

        private static void DecodeOcspRequest(
            byte[] requestBytes,
            out ReadOnlyMemory<byte> certId,
            out ReadOnlyMemory<byte> nonceExtension)
        {
            Asn1Tag context0 = new(TagClass.ContextSpecific, 0);
            Asn1Tag context1 = new(TagClass.ContextSpecific, 1);

            AsnReader reader = new(requestBytes, AsnEncodingRules.DER);
            AsnReader request = reader.ReadSequence();
            reader.ThrowIfNotEmpty();

            AsnReader tbsRequest = request.ReadSequence();

            if (request.HasData)
            {
                // Optional signature
                request.ReadEncodedValue();
                request.ThrowIfNotEmpty();
            }

            // Only v1(0) is supported, and it shouldn't be written per DER.
            // But Apple writes it anyways, so let's go ahead and be lenient.
            if (tbsRequest.PeekTag().HasSameClassAndValue(context0))
            {
                AsnReader versionReader = tbsRequest.ReadSequence(context0);

                if (!versionReader.TryReadInt32(out int version) || version != 0)
                {
                    throw new CryptographicException("ASN1 corrupted data");
                }

                versionReader.ThrowIfNotEmpty();
            }

            if (tbsRequest.PeekTag().HasSameClassAndValue(context1))
            {
                tbsRequest.ReadEncodedValue();
            }

            AsnReader requestList = tbsRequest.ReadSequence();
            AsnReader? requestExtensions = null;

            if (tbsRequest.HasData)
            {
                AsnReader requestExtensionsWrapper = tbsRequest.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 2));
                requestExtensions = requestExtensionsWrapper.ReadSequence();
                requestExtensionsWrapper.ThrowIfNotEmpty();
            }

            tbsRequest.ThrowIfNotEmpty();

            AsnReader firstRequest = requestList.ReadSequence();
            requestList.ThrowIfNotEmpty();

            certId = firstRequest.ReadEncodedValue();

            if (firstRequest.HasData)
            {
                firstRequest.ReadSequence(context0);
            }

            firstRequest.ThrowIfNotEmpty();

            nonceExtension = default;

            if (requestExtensions != null)
            {
                while (requestExtensions.HasData)
                {
                    ReadOnlyMemory<byte> wholeExtension = requestExtensions.PeekEncodedValue();
                    AsnReader extension = requestExtensions.ReadSequence();

                    if (extension.ReadObjectIdentifier() == "1.3.6.1.5.5.7.48.1.2")
                    {
                        nonceExtension = wholeExtension;
                    }
                }
            }
        }
    }
}
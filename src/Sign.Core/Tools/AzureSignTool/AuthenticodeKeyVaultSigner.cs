// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Sign.Core.Interop;

namespace Sign.Core
{
    internal sealed class AuthenticodeKeyVaultSigner : IDisposable
    {
        private readonly RSA _signingAlgorithm;
        private readonly X509Certificate2 _signingCertificate;
        private readonly HashAlgorithmName _fileDigestAlgorithm;
        private readonly TimeStampConfiguration _timeStampConfiguration;
        private readonly MemoryCertificateStore _certificateStore;
        private readonly X509Chain _chain;
        private readonly SignCallback _signCallback;

        public AuthenticodeKeyVaultSigner(
            RSA signingAlgorithm,
            X509Certificate2 signingCertificate,
            HashAlgorithmName fileDigestAlgorithm,
            TimeStampConfiguration timeStampConfiguration,
            X509Certificate2Collection additionalCertificates)
        {
            _fileDigestAlgorithm = fileDigestAlgorithm;
            _signingCertificate = signingCertificate ?? throw new ArgumentNullException(nameof(signingCertificate));
            _timeStampConfiguration = timeStampConfiguration ?? throw new ArgumentNullException(nameof(timeStampConfiguration));
            _signingAlgorithm = signingAlgorithm ?? throw new ArgumentNullException(nameof(signingAlgorithm));
            _certificateStore = MemoryCertificateStore.Create();
            _chain = new X509Chain();

            try
            {
                _chain.ChainPolicy.ExtraStore.AddRange(additionalCertificates);

                // Only build a chain to embed; trust validation is not needed for signing.
                _chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllFlags;

                if (!_chain.Build(signingCertificate))
                {
                    throw new InvalidOperationException("Failed to build chain for certificate.");
                }

                foreach (X509ChainElement element in _chain.ChainElements)
                {
                    _certificateStore.Add(element.Certificate);
                }

                _signCallback = SignCallback;
            }
            catch
            {
                _chain.Dispose();
                _certificateStore.Dispose();
                throw;
            }
        }

        public unsafe int SignFile(
            ReadOnlySpan<char> path,
            ReadOnlySpan<char> description,
            ReadOnlySpan<char> descriptionUrl,
            ILogger logger)
        {
            static char[] NullTerminate(ReadOnlySpan<char> value)
            {
                char[] result = new char[value.Length + 1];
                value.CopyTo(result);
                return result;
            }

            SignerSignEx3Flags flags = SignerSignEx3Flags.SIGN_CALLBACK_UNDOCUMENTED;
            SignerSignTimeStampFlags timeStampFlags = default;
            ReadOnlySpan<byte> timestampAlgorithmOid = default;

            if (_timeStampConfiguration.Url is not null)
            {
                timeStampFlags = SignerSignTimeStampFlags.SIGNER_TIMESTAMP_RFC3161;
                timestampAlgorithmOid = AlgorithmTranslator.HashAlgorithmToOidAsciiTerminated(_timeStampConfiguration.DigestAlgorithm);
            }

            fixed (byte* pTimestampAlgorithm = timestampAlgorithmOid)
            fixed (char* pTimestampUrl = _timeStampConfiguration.Url)
            fixed (char* pPath = NullTerminate(path))
            fixed (char* pDescription = NullTerminate(description))
            fixed (char* pDescriptionUrl = NullTerminate(descriptionUrl))
            {
                SIGNER_FILE_INFO fileInfo = new(pPath);
                uint subjectIndex = 0;
                SIGNER_SUBJECT_INFO subjectInfo = new(&subjectIndex, new SIGNER_SUBJECT_INFO_UNION(&fileInfo));
                SIGNER_ATTR_AUTHCODE authCodeStructure = new(pDescription, pDescriptionUrl);
                SIGNER_CERT_STORE_INFO storeInfo = new(
                    pSigningCert: _signingCertificate.Handle,
                    dwCertPolicy: SignerCertStoreInfoFlags.SIGNER_CERT_POLICY_CHAIN,
                    hCertStore: _certificateStore.Handle);
                SIGNER_CERT signerCert = new(new SIGNER_CERT_UNION(&storeInfo));
                SIGNER_SIGNATURE_INFO signatureInfo = new(
                    AlgorithmTranslator.HashAlgorithmToAlgId(_fileDigestAlgorithm),
                    new SIGNER_SIGNATURE_INFO_UNION(&authCodeStructure));
                SIGN_INFO signCallbackInfo = new(Marshal.GetFunctionPointerForDelegate(_signCallback));

                logger.LogTrace("Getting SIP Data");
                SipKind sipKind = SipExtensionFactory.GetSipKind(path);
                void* sipData = null;
                IntPtr context = IntPtr.Zero;
                APPX_SIP_CLIENT_DATA clientData = default;
                SIGNER_SIGN_EX3_PARAMS parameters = default;

                if (sipKind == SipKind.Appx)
                {
                    clientData.pSignerParams = &parameters;
                    sipData = &clientData;
                    flags |= SignerSignEx3Flags.SPC_EXC_PE_PAGE_HASHES_FLAG;
                    FillAppxExtension(ref clientData, flags, timeStampFlags, &subjectInfo, &signerCert, &signatureInfo,
                        &context, pTimestampUrl, pTimestampAlgorithm, &signCallbackInfo);
                }

                logger.LogTrace("Calling SignerSignEx3 with flags: {Flags}", flags);
                int result = Mssign32.SignerSignEx3(
                    flags,
                    &subjectInfo,
                    &signerCert,
                    &signatureInfo,
                    IntPtr.Zero,
                    timeStampFlags,
                    pTimestampAlgorithm,
                    pTimestampUrl,
                    IntPtr.Zero,
                    sipData,
                    &context,
                    IntPtr.Zero,
                    &signCallbackInfo,
                    IntPtr.Zero);

                if (result == 0 && context != IntPtr.Zero)
                {
                    int freeResult = Mssign32.SignerFreeSignerContext(context);
                    Debug.Assert(freeResult == 0);
                }

                if (result == 0 && sipKind == SipKind.Appx && clientData.pAppxSipState != IntPtr.Zero)
                {
                    Marshal.Release(clientData.pAppxSipState);
                }

                GC.KeepAlive(this);
                return result;
            }
        }

        public void Dispose()
        {
            _chain.Dispose();
            _certificateStore.Dispose();
        }

        private int SignCallback(
            IntPtr pCertContext,
            IntPtr pvExtra,
            uint algId,
            byte[] pDigestToSign,
            uint dwDigestToSign,
            ref CRYPTOAPI_BLOB blob)
        {
            byte[] digest = _signingAlgorithm.SignHash(pDigestToSign, _fileDigestAlgorithm, RSASignaturePadding.Pkcs1);
            IntPtr resultPtr = Marshal.AllocHGlobal(digest.Length);
            Marshal.Copy(digest, 0, resultPtr, digest.Length);
            blob.pbData = resultPtr;
            blob.cbData = (uint)digest.Length;
            return 0;
        }

        private static unsafe void FillAppxExtension(
            ref APPX_SIP_CLIENT_DATA clientData,
            SignerSignEx3Flags flags,
            SignerSignTimeStampFlags timestampFlags,
            SIGNER_SUBJECT_INFO* signerSubjectInfo,
            SIGNER_CERT* signerCert,
            SIGNER_SIGNATURE_INFO* signatureInfo,
            IntPtr* signerContext,
            char* timestampUrl,
            byte* timestampOid,
            SIGN_INFO* signInfo)
        {
            clientData.pSignerParams->dwFlags = flags;
            clientData.pSignerParams->dwTimestampFlags = timestampFlags;
            clientData.pSignerParams->pSubjectInfo = signerSubjectInfo;
            clientData.pSignerParams->pSignerCert = signerCert;
            clientData.pSignerParams->pSignatureInfo = signatureInfo;
            clientData.pSignerParams->ppSignerContext = signerContext;
            clientData.pSignerParams->pwszHttpTimeStamp = timestampUrl;
            clientData.pSignerParams->pszTimestampAlgorithmOid = timestampOid;
            clientData.pSignerParams->pSignCallBack = signInfo;
        }
    }
}

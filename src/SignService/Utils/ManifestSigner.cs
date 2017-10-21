using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using SignService.SigningTools;
using System.Xml;
using System.Deployment.Internal.CodeSigning;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace SignService.Utils
{
   
    static class ManifestSigner
    {
        public static void SignFile(string path, HashMode hashMode, RSA rsaPrivateKey, X509Certificate2 publicCertificate, string timestampUrl)
        {
            var useSha256 = hashMode == HashMode.Sha256;
            try
            {
                XmlDocument manifestDom = new XmlDocument
                {
                    PreserveWhitespace = true
                };
                manifestDom.Load(path);
                SignedCmiManifest2 signedCmiManifest2 = new SignedCmiManifest2(manifestDom, useSha256);
                CmiManifestSigner2 signer = !useSha256 || !(rsaPrivateKey is RSACryptoServiceProvider) ? new CmiManifestSigner2((AsymmetricAlgorithm)rsaPrivateKey, publicCertificate, useSha256) : new CmiManifestSigner2((AsymmetricAlgorithm)SignedCmiManifest2.GetFixedRSACryptoServiceProvider(rsaPrivateKey as RSACryptoServiceProvider, useSha256), publicCertificate, useSha256);
                if (timestampUrl == null)
                    signedCmiManifest2.Sign(signer);
                else
                    signedCmiManifest2.Sign(signer, timestampUrl);
                manifestDom.Save(path);
            }
            catch (Exception ex)
            {
                switch (Marshal.GetHRForException(ex))
                {
                    case -2147012889:
                    case -2147012867:
                        throw new ApplicationException("SecurityUtil.TimestampUrlNotFound", ex);
                    default:
                        throw new ApplicationException(ex.Message, ex);
                }
            }
        }
    }
}

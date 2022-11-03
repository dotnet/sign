using System.Deployment.Internal.CodeSigning;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml;

namespace Sign.Core
{
    internal sealed class ManifestSigner : IManifestSigner
    {
        public void Sign(FileInfo file, X509Certificate2 certificate, RSA rsaPrivateKey, SignOptions options)
        {
            var useSha256 = true;

            try
            {
                XmlDocument manifestDom = new()
                {
                    PreserveWhitespace = true
                };
                manifestDom.Load(file.FullName);
                SignedCmiManifest2 signedCmiManifest2 = new(manifestDom, useSha256);
                CmiManifestSigner2 signer;

                if (useSha256 && rsaPrivateKey is RSACryptoServiceProvider rsaProvider)
                {
                    signer = new CmiManifestSigner2(SignedCmiManifest2.GetFixedRSACryptoServiceProvider(rsaProvider, useSha256), certificate, useSha256);
                }
                else
                {
                    signer = new CmiManifestSigner2(rsaPrivateKey, certificate, useSha256);
                }

                if (options.TimestampService is null)
                {
                    signedCmiManifest2.Sign(signer);
                }
                else
                {
                    signedCmiManifest2.Sign(signer, options.TimestampService.AbsoluteUri);
                }

                manifestDom.Save(file.FullName);
            }
            catch (Exception ex)
            {
                throw Marshal.GetHRForException(ex) switch
                {
                    -2147012889 or -2147012867 => new ApplicationException("TimestampUrlNotFound", ex),
                    _ => new ApplicationException(ex.Message, ex)
                };
            }
        }
    }
}
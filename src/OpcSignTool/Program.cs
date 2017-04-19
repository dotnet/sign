using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Packaging;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;

namespace OpcSignTool
{
    class Program
    {
        static int Main(string[] args)
        {
            // arg 0: thumbprint
            // arg 1: timestamp
            // arg 2: in file
            // arg 3: out file

            if (args.Length != 4)
            {
                Console.Error.WriteLine("Arguments are not correct. Arg 0 = thumbprint, arg 1 = timestamp url, arg 2 = input file, arg 3 = output file.");
                return -1;
            }

            var thumbprint = args[0];

            var cert = FindCertificate(thumbprint, StoreLocation.CurrentUser) ?? FindCertificate(thumbprint, StoreLocation.LocalMachine);

            if (cert == null)
            {
                Console.Error.WriteLine($"Could not locate certificate with thumbprint '{thumbprint}'");
                return -1;
            }

            var timestamp = args[1];
            var inFile = args[2];
            var outFile = args[3];

            if (!File.Exists(inFile))
            {
                Console.Error.WriteLine($"Input file '{inFile}' does not exist");
                return -1;
            }

            try
            {
                // Copy to output location since we sign in-place
                File.Copy(inFile, outFile, true);
                using (var package = Package.Open(outFile))
                {
                    SignAllParts(package, cert, timestamp);
                    if (!ValidateSignatures(package))
                    {
                        Console.WriteLine($"An error has occured signing the package.");
                        return -1;
                    }
                }

            }
            catch (Exception e)
            {
                Console.WriteLine($"An error has occured {e}");
                return -1;
            }

            return 0;
        }

        static X509Certificate2 FindCertificate(string thumbprint, StoreLocation location)
        {
            using (var store = new X509Store(StoreName.My, location))
            {
                store.Open(OpenFlags.ReadOnly);
                var certs = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, true);
                if (certs.Count == 0)
                    return null;

                return certs[0];
            }
        }

        static bool ValidateSignatures(Package package)
        {
            var signatureManager = new PackageDigitalSignatureManager(package);
            return signatureManager.IsSigned && signatureManager.VerifySignatures(true) == VerifyResult.Success;
        }

        static void SignAllParts(Package package, X509Certificate2 certificate, string timestamp)
        {
            var signatureManager = new PackageDigitalSignatureManager(package);
            signatureManager.CertificateOption = CertificateEmbeddingOption.InSignaturePart;
            signatureManager.HashAlgorithm = SignedXml.XmlDsigSHA256Url;
            
            List<Uri> toSign = new List<Uri>();
            foreach (PackagePart packagePart in package.GetParts())
            {
                toSign.Add(packagePart.Uri);
            }

            toSign.Add(PackUriHelper.GetRelationshipPartUri(signatureManager.SignatureOrigin));
            toSign.Add(signatureManager.SignatureOrigin);
            toSign.Add(PackUriHelper.GetRelationshipPartUri(new Uri("/", UriKind.RelativeOrAbsolute)));

            try
            {
                signatureManager.Sign(toSign, certificate);
            }
            catch (System.Security.Cryptography.CryptographicException ex)
            {
                Console.Error.WriteLine("Signing could not be completed: " + ex.Message, "Signing Failure") ;
            }
        }
    }
}
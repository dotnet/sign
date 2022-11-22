using System.Security.Cryptography;

namespace Sign.Core
{
    // This type and its default constructor are public because:
    //   "Algorithms added to CryptoConfig must be accessible from outside their assembly."
    public sealed class RSAPKCS1SHA256SignatureDescription : RSAPKCS1SignatureDescription
    {
        public RSAPKCS1SHA256SignatureDescription()
            : base("SHA256")
        {
        }

        public sealed override HashAlgorithm CreateDigest()
        {
            return SHA256.Create();
        }
    }
}
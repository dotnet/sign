using System.Security.Cryptography;

namespace Sign.Core
{
    internal sealed class RSAPKCS1SHA256SignatureDescription : RSAPKCS1SignatureDescription
    {
        internal RSAPKCS1SHA256SignatureDescription()
            : base("SHA256")
        {
        }

        public sealed override HashAlgorithm CreateDigest()
        {
            return SHA256.Create();
        }
    }
}
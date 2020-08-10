using System.Reflection;

namespace System.Security.Cryptography.Xml
{
    public abstract class RSAPKCS1SignatureDescription : SignatureDescription
    {
        public RSAPKCS1SignatureDescription(string hashAlgorithmName)
        {
            KeyAlgorithm = typeof(RSA).AssemblyQualifiedName;
            FormatterAlgorithm = typeof(RSAPKCS1SignatureFormatter).AssemblyQualifiedName;
            DeformatterAlgorithm = typeof(RSAPKCS1SignatureDeformatter).AssemblyQualifiedName;
            DigestAlgorithm = hashAlgorithmName;
        }

        public sealed override AsymmetricSignatureDeformatter CreateDeformatter(AsymmetricAlgorithm key)
        {
            var item = (AsymmetricSignatureDeformatter)CryptoConfig.CreateFromName(DeformatterAlgorithm);
            item.SetKey(key);
            item.SetHashAlgorithm(DigestAlgorithm);
            return item;
        }

        public sealed override AsymmetricSignatureFormatter CreateFormatter(AsymmetricAlgorithm key)
        {
            var item = (AsymmetricSignatureFormatter)CryptoConfig.CreateFromName(FormatterAlgorithm);
            item.SetKey(key);
            item.SetHashAlgorithm(DigestAlgorithm);
            return item;
        }

        public abstract override HashAlgorithm CreateDigest();
    }

    public class RSAPKCS1SHA256SignatureDescription : RSAPKCS1SignatureDescription
    {
        public RSAPKCS1SHA256SignatureDescription() : base("SHA256")
        {
        }

        public sealed override HashAlgorithm CreateDigest()
        {
            return SHA256.Create();
        }
    }
}

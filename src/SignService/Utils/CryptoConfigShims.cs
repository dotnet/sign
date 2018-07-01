using System.Reflection;

namespace System.Security.Cryptography.Xml
{
    public abstract class RSAPKCS1SignatureDescription : SignatureDescription
    {
        static MethodInfo CryptoHelpersCreateFromName;

        static RSAPKCS1SignatureDescription()
        {
            // Get the CryptoHelpers impl
            var helperType = Type.GetType("System.Security.Cryptography.Xml.CryptoHelpers, System.Security.Cryptography.Xml");

            CryptoHelpersCreateFromName = helperType.GetTypeInfo().GetDeclaredMethod("CreateFromName");
        }

        public RSAPKCS1SignatureDescription(string hashAlgorithmName)
        {
            KeyAlgorithm = typeof(RSA).AssemblyQualifiedName;
            FormatterAlgorithm = typeof(RSAPKCS1SignatureFormatter).AssemblyQualifiedName;
            DeformatterAlgorithm = typeof(RSAPKCS1SignatureDeformatter).AssemblyQualifiedName;
            DigestAlgorithm = hashAlgorithmName;
        }

        public sealed override AsymmetricSignatureDeformatter CreateDeformatter(AsymmetricAlgorithm key)
        {
            var item = (AsymmetricSignatureDeformatter)CryptoHelpersCreateFromName.Invoke(null, new object[] { DeformatterAlgorithm });
            item.SetKey(key);
            item.SetHashAlgorithm(DigestAlgorithm);
            return item;
        }

        public sealed override AsymmetricSignatureFormatter CreateFormatter(AsymmetricAlgorithm key)
        {
            var item = (AsymmetricSignatureFormatter)CryptoHelpersCreateFromName.Invoke(null, new object[] { FormatterAlgorithm });
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
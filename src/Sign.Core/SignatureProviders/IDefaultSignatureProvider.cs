namespace Sign.Core
{
    internal interface IDefaultSignatureProvider
    {
        ISignatureProvider SignatureProvider { get; }
    }
}
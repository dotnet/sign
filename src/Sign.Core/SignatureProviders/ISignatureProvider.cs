namespace Sign.Core
{
    internal interface ISignatureProvider
    {
        bool CanSign(FileInfo file);
        Task SignAsync(IEnumerable<FileInfo> files, SignOptions options);
    }
}
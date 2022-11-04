namespace Sign.Core
{
    internal interface ITemporaryDirectory : IDisposable
    {
        DirectoryInfo Directory { get; }
    }
}
namespace Sign.Core
{
    internal interface IDirectoryService : IDisposable
    {
        DirectoryInfo CreateTemporaryDirectory();
        void Delete(DirectoryInfo directory);
    }
}
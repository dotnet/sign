namespace Sign.Core
{
    internal interface IContainerProvider
    {
        bool IsAppxBundleContainer(FileInfo file);
        bool IsAppxContainer(FileInfo file);
        bool IsZipContainer(FileInfo file);
        IContainer? GetContainer(FileInfo file);
    }
}
namespace Sign.Core
{
    internal interface IFileMetadataService
    {
        bool IsPortableExecutable(FileInfo file);
    }
}
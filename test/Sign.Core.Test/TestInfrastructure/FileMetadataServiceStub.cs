namespace Sign.Core.Test
{
    internal sealed class FileMetadataServiceStub : IFileMetadataService
    {
        internal List<FileInfo> PortableExecutableFiles { get; } = new();

        public bool IsPortableExecutable(FileInfo file)
        {
            return PortableExecutableFiles.Contains(file, FileInfoComparer.Instance);
        }
    }
}
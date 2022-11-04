namespace Sign.Core
{
    internal sealed class FileMetadataService : IFileMetadataService
    {
        public bool IsPortableExecutable(FileInfo file)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));

            using (FileStream stream = file.OpenRead())
            {
                var buffer = new byte[2];
                if (stream.CanRead)
                {
                    var read = stream.Read(buffer, 0, 2);
                    if (read == 2)
                    {
                        // Look for the magic MZ header 
                        return buffer[0] == 0x4d && buffer[1] == 0x5a;
                    }
                }
            }

            return false;
        }
    }
}
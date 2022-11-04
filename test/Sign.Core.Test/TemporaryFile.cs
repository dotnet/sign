namespace Sign.Core.Test
{
    internal sealed class TemporaryFile : IDisposable
    {
        internal FileInfo File { get; }

        internal TemporaryFile()
        {
            File = new(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));
        }

        public void Dispose()
        {
            File.Refresh();

            if (File.Exists)
            {
                File.Delete();

                File.Refresh();
            }
        }
    }
}
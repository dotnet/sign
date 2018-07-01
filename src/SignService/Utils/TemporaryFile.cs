using System;
using System.IO;

namespace SignService
{
    public class TemporaryFile : IDisposable
    {
        public TemporaryFile()
        {
            FileName = Path.GetTempFileName();
        }

        public string FileName { get; }
        bool disposed;

        public void Dispose()
        {
            if (!disposed)
            {
                disposed = true;
                try
                {
                    File.Delete(FileName);
                }
                catch // best effort
                {
                }
            }
        }

    }
}

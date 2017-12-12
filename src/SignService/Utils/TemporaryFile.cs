using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SignService.Utils;
using Wyam.Core.IO.Globbing;

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

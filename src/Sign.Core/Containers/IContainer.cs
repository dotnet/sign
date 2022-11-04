using Microsoft.Extensions.FileSystemGlobbing;

namespace Sign.Core
{
    internal interface IContainer : IDisposable
    {
        IEnumerable<FileInfo> GetFiles();
        IEnumerable<FileInfo> GetFiles(Matcher matcher);

        ValueTask OpenAsync();
        ValueTask SaveAsync();
    }
}
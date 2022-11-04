using Microsoft.Extensions.FileSystemGlobbing;

namespace Sign.Core
{
    internal interface IFileListReader
    {
        void Read(StreamReader reader, out Matcher matcher, out Matcher antiMatcher);
    }
}
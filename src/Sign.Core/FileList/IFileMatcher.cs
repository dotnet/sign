using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace Sign.Core
{
    internal interface IFileMatcher
    {
        IEnumerable<FileInfo> EnumerateMatches(DirectoryInfoBase directory, Matcher matcher);
    }
}
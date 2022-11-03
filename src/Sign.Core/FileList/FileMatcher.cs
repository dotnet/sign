using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace Sign.Core
{
    internal sealed class FileMatcher : IFileMatcher
    {
        private readonly Func<string, string> _normalizeSlashes;

        public FileMatcher()
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                _normalizeSlashes = (string path) => path.Replace('/', Path.DirectorySeparatorChar);
            }
            else
            {
                _normalizeSlashes = _ => _;
            }
        }

        public IEnumerable<FileInfo> EnumerateMatches(DirectoryInfoBase directory, Matcher matcher)
        {
            ArgumentNullException.ThrowIfNull(directory, nameof(directory));
            ArgumentNullException.ThrowIfNull(matcher, nameof(matcher));

            PatternMatchingResult result = matcher.Execute(directory);

            return result.Files.Select(file => new FileInfo(Path.Combine(directory.FullName, _normalizeSlashes(file.Path))));
        }
    }
}
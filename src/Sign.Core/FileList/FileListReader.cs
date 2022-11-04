using Microsoft.Extensions.FileSystemGlobbing;

namespace Sign.Core
{
    internal sealed class FileListReader : IFileListReader
    {
        private readonly IMatcherFactory _matcherFactory;

        // Dependency injection requires a public constructor.
        public FileListReader(IMatcherFactory matcherFactory)
        {
            ArgumentNullException.ThrowIfNull(matcherFactory, nameof(matcherFactory));

            _matcherFactory = matcherFactory;
        }

        public void Read(StreamReader reader, out Matcher matcher, out Matcher antiMatcher)
        {
            ArgumentNullException.ThrowIfNull(reader);

            List<string> globs = new();
            List<string> antiglobs = new();
            string? line;

            while ((line = reader.ReadLine()) is not null)
            {
                // don't allow parent directory traversal
                line = line.Replace(@"..\", "").Replace("../", "");

                if (!string.IsNullOrWhiteSpace(line))
                {
                    if (line.StartsWith("!"))
                    {
                        antiglobs.Add(line[1..]);
                    }
                    else
                    {
                        globs.Add(line);
                    }
                }
            }

            matcher = Globber.CreateMatcher(_matcherFactory, globs);
            antiMatcher = Globber.CreateMatcher(_matcherFactory, antiglobs);
        }
    }
}
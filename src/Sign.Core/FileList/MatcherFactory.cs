using Microsoft.Extensions.FileSystemGlobbing;

namespace Sign.Core
{
    internal sealed class MatcherFactory : IMatcherFactory
    {
        internal StringComparison StringComparison { get; } = StringComparison.OrdinalIgnoreCase;

        public Matcher Create()
        {
            return new Matcher(StringComparison);
        }
    }
}
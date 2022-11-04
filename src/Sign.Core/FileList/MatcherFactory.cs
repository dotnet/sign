using Microsoft.Extensions.FileSystemGlobbing;

namespace Sign.Core
{
    internal sealed class MatcherFactory : IMatcherFactory
    {
        public Matcher Create()
        {
            return new Matcher(StringComparison.OrdinalIgnoreCase);
        }
    }
}
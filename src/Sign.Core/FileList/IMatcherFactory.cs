using Microsoft.Extensions.FileSystemGlobbing;

namespace Sign.Core
{
    internal interface IMatcherFactory
    {
        Matcher Create();
    }
}
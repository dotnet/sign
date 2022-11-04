using Microsoft.Extensions.Logging;

namespace Sign.Core
{
    internal sealed class MakeAppxCli : CliTool, IMakeAppxCli
    {
        // Dependency injection requires a public constructor.
        public MakeAppxCli(IToolConfigurationProvider toolConfigurationProvider, ILogger<IMakeAppxCli> logger)
            : base(toolConfigurationProvider.MakeAppx, logger)
        {
        }
    }
}
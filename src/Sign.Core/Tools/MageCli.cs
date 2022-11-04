using Microsoft.Extensions.Logging;

namespace Sign.Core
{
    internal sealed class MageCli : CliTool, IMageCli
    {
        // Dependency injection requires a public constructor.
        public MageCli(IToolConfigurationProvider toolConfigurationProvider, ILogger<IMageCli> logger)
            : base(toolConfigurationProvider.Mage, logger)
        {
        }
    }
}
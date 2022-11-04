using Microsoft.Extensions.Logging;

namespace Sign.Core
{
    internal abstract class Tool
    {
        protected ILogger Logger { get; }

        internal Tool(ILogger<ITool> logger)
        {
            ArgumentNullException.ThrowIfNull(logger, nameof(logger));

            Logger = logger;
        }
    }
}
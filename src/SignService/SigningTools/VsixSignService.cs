using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SignService.SigningTools
{
    public class VsixSignService : ICodeSignService
    {
        ISigningToolAggregate aggregate;
        OpcSignService opcSignService;
        readonly IServiceProvider serviceProvider;
        readonly ILogger<VsixSignService> logger;

        public VsixSignService(IServiceProvider serviceProvider, ILogger<VsixSignService> logger)
        {
            this.serviceProvider = serviceProvider;
            this.logger = logger;
        }
        public async Task Submit(HashMode hashMode, string name, string description, string descriptionUrl, IList<string> files)
        {
            if (opcSignService == null) opcSignService = serviceProvider.GetService<OpcSignService>();
            if(aggregate == null) aggregate = serviceProvider.GetService<ISigningToolAggregate>();

            // TODO: crack open the VISX and pass the contents to the aggregator

            await opcSignService.Submit(hashMode, name, description, descriptionUrl, files);
        }

        public IReadOnlyCollection<string> SupportedFileExtensions { get; } = new List<string>
        {
            ".vsix"
        };
        public bool IsDefault { get; }
    }
}

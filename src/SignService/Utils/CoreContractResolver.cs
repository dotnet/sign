using System;
using Newtonsoft.Json.Serialization;

namespace SignService.Utils
{
    public sealed class CoreContractResolver : DefaultContractResolver
    {
        readonly IServiceProvider provider;

        public CoreContractResolver(IServiceProvider provider)
        {
            this.provider = provider;
        }

        protected override JsonObjectContract CreateObjectContract(Type objectType)
        {
            var contract = base.CreateObjectContract(objectType);
            var svc = provider.GetService(objectType);
            if (svc != null)
            {
                contract.DefaultCreator = () => provider.GetService(objectType);
            }

            return contract;
        }

    }
}

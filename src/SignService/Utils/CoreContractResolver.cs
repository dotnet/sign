using System;
using Newtonsoft.Json.Serialization;

namespace SignService.Utils
{
    public sealed class CoreContractResolver : DefaultContractResolver
    {
        readonly IServiceProvider _provider;

        public CoreContractResolver(IServiceProvider provider)
        {
            _provider = provider;
        }

        protected override JsonObjectContract CreateObjectContract(Type objectType)
        {
            var contract = base.CreateObjectContract(objectType);
            var svc = _provider.GetService(objectType);
            if (svc != null)
            {
                contract.DefaultCreator = () => _provider.GetService(objectType);
            }

            return contract;
        }

    }
}
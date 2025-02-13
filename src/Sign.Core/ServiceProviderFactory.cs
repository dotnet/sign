// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Sign.Core
{
    internal sealed class ServiceProviderFactory : IServiceProviderFactory
    {
        private Action<IServiceCollection>? _addServices;

        public IServiceProvider Create(
            LogLevel logLevel = LogLevel.Information,
            ILoggerProvider? loggerProvider = null,
            Action<IServiceCollection>? addServices = null)
        {

            if (_addServices is not null)
            {
                addServices += _addServices;
            }

            return ServiceProvider.CreateDefault(logLevel, loggerProvider, addServices);
        }

        public void AddServices(Action<IServiceCollection> addServices)
        {
            ArgumentNullException.ThrowIfNull(addServices, nameof(addServices));

            _addServices += addServices;
        }
    }
}

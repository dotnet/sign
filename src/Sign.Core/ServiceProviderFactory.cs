// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Sign.Core
{
    internal sealed class ServiceProviderFactory : IServiceProviderFactory
    {
        private Action<IServiceCollection>? servicesToAdd;

        public IServiceProvider Create(
            LogLevel logLevel = LogLevel.Information,
            ILoggerProvider? loggerProvider = null,
            Action<IServiceCollection>? addServices = null)
        {

            if (servicesToAdd is not null)
            {
                addServices += servicesToAdd;
            }

            return ServiceProvider.CreateDefault(logLevel, loggerProvider, addServices);
        }

        public void AddServices(Action<IServiceCollection> services)
        {
            ArgumentNullException.ThrowIfNull(services, nameof(services));

            servicesToAdd += services;
        }
    }
}

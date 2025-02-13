// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Sign.Core.Test
{
    public class ServiceProviderFactoryTests
    {
        [Fact]
        public void AddService_ServicesIsNull_Throws()
        {
            var factory = new ServiceProviderFactory();

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => factory.AddServices(null!));

            Assert.Equal("addServices", exception.ParamName);
        }

        [Fact]
        public void AddServices_WhenServicesAreNotAlreadyPresent_AddsServices()
        {
            var factory = new ServiceProviderFactory();
            factory.AddServices(services => services.AddSingleton<ITestService, TestService>());
            IServiceProvider serviceProvider = factory.Create();
            Assert.NotNull(serviceProvider.GetRequiredService<ITestService>());
        }

        [Fact]
        public void AddServices_WhenSameServiceIsNotAlreadyPresent_AddsService()
        {
            var factory = new ServiceProviderFactory();
            factory.AddServices(services => services.AddSingleton<ITestService, TestService>());
            IServiceProvider serviceProvider = factory.Create(addServices: services => services.AddSingleton<ITestService2, TestService2>());
            Assert.NotNull(serviceProvider.GetRequiredService<ITestService>());
            Assert.NotNull(serviceProvider.GetRequiredService<ITestService2>());
        }

        [Fact]
        public void AddServices_WhenSameServiceIsAlreadyPresent_AddsService()
        {
            var factory = new ServiceProviderFactory();
            factory.AddServices(services => services.AddSingleton<ITestService, TestService>());
            IServiceProvider serviceProvider = factory.Create(addServices: services => services.AddSingleton<ITestService, TestService>());
            Assert.NotNull(serviceProvider.GetRequiredService<ITestService>());
        }

        [Fact]
        public void Create_WhenNoServicesAdded_ReturnsDefault()
        {
            var factory = new ServiceProviderFactory();
            IServiceProvider serviceProvider = factory.Create();
            Assert.NotNull(serviceProvider.GetRequiredService<ILogger<ServiceProviderFactoryTests>>());
        }
    }

    public interface ITestService
    {
    }

    public class TestService : ITestService
    {
    }

    public interface ITestService2
    {
    }

    public class TestService2 : ITestService2
    {
    }
}

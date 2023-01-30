// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Configuration;

namespace Sign.Core
{
    internal sealed class ServiceProvider : IServiceProvider
    {
        private readonly IServiceProvider _serviceProvider;

        // Dependency injection requires a public constructor.
        public ServiceProvider(IServiceProvider serviceProvider)
        {
            ArgumentNullException.ThrowIfNull(serviceProvider, nameof(serviceProvider));

            _serviceProvider = serviceProvider;
        }

        public object? GetService(Type serviceType)
        {
            return _serviceProvider.GetService(serviceType);
        }

        internal static ServiceProvider CreateDefault(
            LogLevel logLevel = LogLevel.Information,
            ILoggerProvider? loggerProvider = null)
        {
            IServiceCollection services = new ServiceCollection();
            IConfigurationBuilder configurationBuilder = new ConfigurationBuilder();
            AppRootDirectoryLocator locator = new();

            configurationBuilder.SetBasePath(locator.Directory.FullName)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .AddEnvironmentVariables();

            IConfiguration configuration = configurationBuilder.Build();
            IConfigurationSection loggingSection = configuration.GetSection("Logging");

            services.AddLogging(builder =>
            {
                builder = builder.SetMinimumLevel(logLevel)
                    .AddConfiguration(loggingSection)
                    .AddConsole();

                if (loggerProvider is not null)
                {
                    builder.AddProvider(loggerProvider);
                }
            });

            services.AddSingleton<IAppRootDirectoryLocator, AppRootDirectoryLocator>();
            services.AddSingleton<IToolConfigurationProvider, ToolConfigurationProvider>();
            services.AddSingleton<IMatcherFactory, MatcherFactory>();
            services.AddSingleton<IFileListReader, FileListReader>();
            services.AddSingleton<IFileMatcher, FileMatcher>();
            services.AddSingleton<IContainerProvider, ContainerProvider>();
            services.AddSingleton<IFileMetadataService, FileMetadataService>();
            services.AddSingleton<IDirectoryService, DirectoryService>();
            services.AddSingleton<IKeyVaultService, KeyVaultService>();
            services.AddSingleton<ISignatureProvider, AzureSignToolSignatureProvider>();
            services.AddSingleton<ISignatureProvider, ClickOnceSignatureProvider>();
            services.AddSingleton<ISignatureProvider, VsixSignatureProvider>();
            services.AddSingleton<ISignatureProvider, NuGetSignatureProvider>();
            services.AddSingleton<ISignatureProvider, AppInstallerServiceSignatureProvider>();
            services.AddSingleton<IDefaultSignatureProvider, DefaultSignatureProvider>();
            services.AddSingleton<IAggregatingSignatureProvider, AggregatingSignatureProvider>();
            services.AddSingleton<IManifestSigner, ManifestSigner>();
            services.AddSingleton<IMageCli, MageCli>();
            services.AddSingleton<IMakeAppxCli, MakeAppxCli>();
            services.AddSingleton<INuGetSignTool, NuGetSignTool>();
            services.AddSingleton<IOpenVsixSignTool, OpenVsixSignTool>();
            services.AddSingleton<ICertificateVerifier, CertificateVerifier>();
            services.AddSingleton<ISigner, Signer>();

            return new ServiceProvider(services.BuildServiceProvider());
        }
    }
}
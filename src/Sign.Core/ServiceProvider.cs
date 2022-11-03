using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Sign.Core
{
    internal sealed class ServiceProvider : IServiceProvider
    {
        private readonly IServiceProvider _serviceProvider;

        private ServiceProvider(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public object? GetService(Type serviceType)
        {
            return _serviceProvider.GetService(serviceType);
        }

        internal static ServiceProvider CreateForCli(LogLevel logLevel = LogLevel.Information)
        {
            IServiceCollection services = new ServiceCollection();

            services.AddLogging(builder =>
            {
                builder.SetMinimumLevel(logLevel);
            });

            services.AddSingleton<IMatcherFactory, MatcherFactory>();
            services.AddSingleton<IFileListReader, FileListReader>();
            services.AddSingleton<IFileMatcher, FileMatcher>();

            return new ServiceProvider(services.BuildServiceProvider());
        }

        internal static ServiceProvider CreateDefault(LogLevel logLevel = LogLevel.Information)
        {
            IServiceCollection services = new ServiceCollection();

            services.AddLogging(builder =>
            {
                builder.SetMinimumLevel(logLevel);
            });

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

            return new ServiceProvider(services.BuildServiceProvider());
        }
    }
}
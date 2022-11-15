using Microsoft.Extensions.Logging;

namespace Sign.Core
{
    internal sealed class ContainerProvider : IContainerProvider
    {
        private readonly HashSet<string> _appxBundleExtensions;
        private readonly HashSet<string> _appxExtensions;
        private readonly IDirectoryService _directoryService;
        private readonly IFileMatcher _fileMatcher;
        private readonly IKeyVaultService _keyVaultService;
        private readonly ILogger _logger;
        private readonly IMakeAppxCli _makeAppxCli;
        private readonly HashSet<string> _zipExtensions;

        // Dependency injection requires a public constructor.
        public ContainerProvider(
            IKeyVaultService keyVaultService,
            IDirectoryService directoryService,
            IFileMatcher fileMatcher,
            IMakeAppxCli makeAppxCli,
            ILogger<IDirectoryService> logger)
        {
            ArgumentNullException.ThrowIfNull(keyVaultService, nameof(keyVaultService));
            ArgumentNullException.ThrowIfNull(directoryService, nameof(directoryService));
            ArgumentNullException.ThrowIfNull(fileMatcher, nameof(fileMatcher));
            ArgumentNullException.ThrowIfNull(makeAppxCli, nameof(makeAppxCli));
            ArgumentNullException.ThrowIfNull(logger, nameof(logger));

            _keyVaultService = keyVaultService;
            _directoryService = directoryService;
            _fileMatcher = fileMatcher;
            _makeAppxCli = makeAppxCli;
            _logger = logger;

            _appxBundleExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".appxbundle",
                ".eappxbundle",
                ".emsixbundle",
                ".msixbundle"
            };

            _appxExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".appx",
                ".eappx",
                ".emsix",
                ".msix"
            };

            _zipExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".appxupload",
                ".msixupload",
                ".nupkg",
                ".snupkg",
                ".vsix",
                ".zip"
            };
        }

        public bool IsAppxBundleContainer(FileInfo file)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));

            return _appxBundleExtensions.Contains(file.Extension);
        }

        public bool IsAppxContainer(FileInfo file)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));

            return _appxExtensions.Contains(file.Extension);
        }

        public bool IsZipContainer(FileInfo file)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));

            return _zipExtensions.Contains(file.Extension);
        }

        public IContainer? GetContainer(FileInfo file)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));

            if (IsAppxBundleContainer(file))
            {
                return new AppxBundleContainer(file, _directoryService, _fileMatcher, _makeAppxCli, _logger);
            }

            if (IsAppxContainer(file))
            {
                return new AppxContainer(file, _keyVaultService, _directoryService, _fileMatcher, _makeAppxCli, _logger);
            }

            if (IsZipContainer(file))
            {
                return new ZipContainer(file, _directoryService, _fileMatcher, _logger);
            }

            return null;
        }
    }
}
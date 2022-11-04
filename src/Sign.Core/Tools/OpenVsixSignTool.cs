using Microsoft.Extensions.Logging;
using OpenVsixSignTool.Core;

namespace Sign.Core
{
    internal sealed class OpenVsixSignTool : Tool, IOpenVsixSignTool
    {
        private readonly IKeyVaultService _keyVaultService;

        // Dependency injection requires a public constructor.
        public OpenVsixSignTool(
            IKeyVaultService keyVaultService,
            IToolConfigurationProvider toolConfigurationProvider,
            ILogger<INuGetSignTool> logger)
            : base(logger)
        {
            ArgumentNullException.ThrowIfNull(keyVaultService, nameof(keyVaultService));

            _keyVaultService = keyVaultService;
        }

        public async Task<bool> SignAsync(FileInfo file, SignConfigurationSet configuration, SignOptions options)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));
            ArgumentNullException.ThrowIfNull(options, nameof(options));

            var failed = false;

            // Append a signature
            using (OpcPackage package = OpcPackage.Open(file.FullName, OpcPackageFileMode.ReadWrite))
            {
                Logger.LogInformation("Signing {fileName}", file.FullName);

                OpcPackageSignatureBuilder signBuilder = package.CreateSignatureBuilder();
                signBuilder.EnqueueNamedPreset<VSIXSignatureBuilderPreset>();

                OpcSignature signature = signBuilder.Sign(configuration);

                if (options.TimestampService is not null)
                {
                    OpcPackageTimestampBuilder timestampBuilder = signature.CreateTimestampBuilder();
                    TimestampResult result = await timestampBuilder.SignAsync(options.TimestampService, options.TimestampHashAlgorithm);

                    if (result == TimestampResult.Failed)
                    {
                        failed = true;
                        Logger.LogError("Error timestamping VSIX");
                    }
                }
            }

            return !failed;
        }
    }
}
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.Logging;
using OpenVsixSignTool.Core;

namespace Sign.Core
{
    internal sealed class OpenVsixSignTool : Tool, IOpenVsixSignTool
    {
        // Dependency injection requires a public constructor.
        public OpenVsixSignTool(ILogger<INuGetSignTool> logger)
            : base(logger)
        {
        }

        public async Task<bool> SignAsync(FileInfo file, SignConfigurationSet configuration, SignOptions options)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));
            ArgumentNullException.ThrowIfNull(options, nameof(options));

            var failed = false;

            // Append a signature
            using (OpcPackage package = OpcPackage.Open(file.FullName, OpcPackageFileMode.ReadWrite))
            {
                Logger.LogInformation(Resources.SigningFile, file.FullName);

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
                        Logger.LogError(Resources.ErrorSigningVsix);
                    }
                }
            }

            return !failed;
        }
    }
}
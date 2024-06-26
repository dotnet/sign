// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Sign.Core;

internal class Exporter : IExporter
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<IExporter> _logger;

    // Dependency injection requires a public constructor.
    public Exporter(IServiceProvider serviceProvider, ILogger<IExporter> logger)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider, nameof(serviceProvider));
        ArgumentNullException.ThrowIfNull(logger, nameof(logger));

        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<int> ExportAsync(string outputPath)
    {
        try
        {
            ICertificateProvider certificateProvider = _serviceProvider.GetRequiredService<ICertificateProvider>();
            using (X509Certificate2 certificate = await certificateProvider.GetCertificateAsync())
            {
                _logger.LogInformation(Resources.FetchedCertificateFingerprint, certificate.Thumbprint);

                // Write out copy of public part of certificate if an output path was given
                if (!string.IsNullOrWhiteSpace(outputPath))
                {
                    // Ensure parent directory exists
                    string? parentDir = Path.GetDirectoryName(outputPath);
                    if (parentDir is not null && !Directory.Exists(parentDir))
                    {
                        _logger.LogDebug(Resources.CreatingDirectory, parentDir);
                        Directory.CreateDirectory(parentDir);
                    }

                    // Clean up any existing file
                    if (File.Exists(outputPath))
                    {
                        _logger.LogDebug(Resources.DeletingFile, outputPath);
                        File.Delete(outputPath);
                        _logger.LogDebug(Resources.DeletedFile, outputPath);
                    }

                    _logger.LogInformation(Resources.ExportingCertificate, outputPath);

                    var sw = Stopwatch.StartNew();
                    byte[] data = certificate.Export(X509ContentType.Cert);
                    await File.WriteAllBytesAsync(outputPath, data);
                    _logger.LogInformation(Resources.ExportSucceededWithTimeElapsed, sw.ElapsedMilliseconds);
                }
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            return ExitCode.Failed;
        }

        return ExitCode.Success;
    }
}

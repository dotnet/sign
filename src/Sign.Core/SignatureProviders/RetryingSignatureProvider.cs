// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

namespace Sign.Core
{
    internal abstract class RetryingSignatureProvider
    {
        protected ILogger Logger { get; }

        protected RetryingSignatureProvider(ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(logger, nameof(logger));

            Logger = logger;
        }

        protected abstract Task<bool> SignCoreAsync(string? args, FileInfo file, RSA rsaPrivateKey, X509Certificate2 certificate, SignOptions options);

        // Inspired from https://github.com/squaredup/bettersigntool/blob/master/bettersigntool/bettersigntool/SignCommand.cs
        protected async Task<bool> SignAsync(string? args, FileInfo file, RSA rsaPrivateKey, X509Certificate2 publicCertificate, SignOptions options)
        {
            var retry = TimeSpan.FromSeconds(5);
            var attempt = 1;
            do
            {
                if (attempt > 1)
                {
                    Logger.LogInformation("Performing attempt #{attempt} of 3 attempts after {seconds}s", attempt, retry.TotalSeconds);
                    await Task.Delay(retry);
                    retry = TimeSpan.FromSeconds(Math.Pow(retry.TotalSeconds, 1.5));
                }

                if (await SignCoreAsync(args, file, rsaPrivateKey, publicCertificate, options))
                {
                    Logger.LogInformation($"Signed successfully");
                    return true;
                }

                attempt++;

            } while (attempt <= 3);

            Logger.LogError($"Failed to sign. Attempts exceeded");

            return false;
        }
    }
}
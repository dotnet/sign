// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Sign.Core
{
    internal abstract class CliTool : Tool, ICliTool
    {
        protected FileInfo Cli { get; }

        internal CliTool(FileInfo cli, ILogger<ITool> logger)
            : base(logger)
        {
            ArgumentNullException.ThrowIfNull(cli, nameof(cli));

            Cli = cli;
        }

        public async Task<int> RunAsync(string? args)
        {
            using (Process process = new())
            {
                process.StartInfo = new ProcessStartInfo()
                {
                    FileName = Cli.FullName,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    Arguments = args
                };

                Logger.LogInformation("Running {fileName} with parameters: '{args}'", Cli.Name, args);

                process.Start();

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();

                Logger.LogInformation("{fileName} Out {output}", Cli.Name, output);

                if (!string.IsNullOrWhiteSpace(error))
                {
                    Logger.LogInformation("{fileName} Err {error}", Cli.Name, error);
                }

                using (CancellationTokenSource cancellationTokenSource = new())
                {
                    cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(30));

                    await process.WaitForExitAsync(cancellationTokenSource.Token);

                    if (cancellationTokenSource.IsCancellationRequested)
                    {
                        Logger.LogError("Error: {fileName} took too long to respond {exitCode}", Cli.Name, process.ExitCode);

                        try
                        {
                            process.Kill();
                        }
                        catch (Exception ex)
                        {
                            throw new Exception($"{Cli.Name} timed out and could not be killed", ex);
                        }

                        Logger.LogError("Error: {fileName} took too long to respond {exitCode}", Cli.Name, process.ExitCode);
                        throw new Exception($"{Cli.Name} took too long to respond with {process.StartInfo.Arguments}");
                    }

                    return process.ExitCode;
                }
            }
        }
    }
}
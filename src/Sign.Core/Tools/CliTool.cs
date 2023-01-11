// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Diagnostics;
using System.Globalization;
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

                Logger.LogInformation(Resources.RunningCli, Cli.Name, args);

                process.Start();

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();

                Logger.LogInformation(Resources.CliStandardOutput, Cli.Name, output);

                if (!string.IsNullOrWhiteSpace(error))
                {
                    Logger.LogInformation(Resources.CliStandardError, Cli.Name, error);
                }

                using (CancellationTokenSource cancellationTokenSource = new())
                {
                    cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(30));

                    await process.WaitForExitAsync(cancellationTokenSource.Token);

                    if (cancellationTokenSource.IsCancellationRequested)
                    {
                        Logger.LogError(Resources.ProcessDidNotExitInTime, Cli.Name, process.ExitCode);

                        string message;

                        try
                        {
                            process.Kill();
                        }
                        catch (Exception ex)
                        {
                            message = string.Format(CultureInfo.CurrentCulture, Resources.ProcessCouldNotBeKilled, Cli.Name);

                            throw new Exception(message, ex);
                        }

                        Logger.LogError(Resources.ProcessDidNotExitInTime, Cli.Name, process.ExitCode);

                        message = string.Format(
                            CultureInfo.CurrentCulture,
                            Resources.ProcessDidNotExitInTimeWithArguments,
                            Cli.Name,
                            process.ExitCode,
                            process.StartInfo.Arguments);

                        throw new Exception(message);
                    }

                    return process.ExitCode;
                }
            }
        }
    }
}
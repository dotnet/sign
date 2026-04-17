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

        internal CliTool(
            FileInfo cli,
            ILogger<ICliTool> logger)
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
                    RedirectStandardInput = true,
                    Arguments = args
                };

                Logger.LogInformation(Resources.RunningCli, Cli.Name, args);

                process.Start();

                // Close stdin so the child process cannot block waiting for input.
                process.StandardInput.Close();

                // Read stdout and stderr concurrently to avoid a deadlock where the child
                // process blocks writing to a full pipe buffer while this process blocks
                // waiting for the other stream's ReadToEnd to complete.
                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();

                await Task.WhenAll(outputTask, errorTask);

                string output = outputTask.Result;
                string error = errorTask.Result;

                Logger.LogInformation(Resources.CliStandardOutput, Cli.Name, output);

                if (!string.IsNullOrWhiteSpace(error))
                {
                    Logger.LogInformation(Resources.CliStandardError, Cli.Name, error);
                }

                using (CancellationTokenSource cancellationTokenSource = new())
                {
                    cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(30));

                    try
                    {
                        await process.WaitForExitAsync(cancellationTokenSource.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        // The process has not exited, so process.ExitCode is unavailable.
                        int exitCode = ExitCode.Failed;

                        Logger.LogError(Resources.ProcessDidNotExitInTime, Cli.Name, exitCode);

                        try
                        {
                            process.Kill();
                        }
                        catch (Exception ex)
                        {
                            string killMessage = string.Format(CultureInfo.CurrentCulture, Resources.ProcessCouldNotBeKilled, Cli.Name);

                            throw new Exception(killMessage, ex);
                        }

                        string message = string.Format(
                            CultureInfo.CurrentCulture,
                            Resources.ProcessDidNotExitInTimeWithArguments,
                            Cli.Name,
                            exitCode,
                            process.StartInfo.Arguments);

                        throw new Exception(message);
                    }

                    return process.ExitCode;
                }
            }
        }
    }
}
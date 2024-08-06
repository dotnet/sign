// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.Logging;
using NuGet.Common;
using LogLevel = NuGet.Common.LogLevel;

namespace Sign.Core
{
    internal sealed class NuGetLogger : NuGet.Common.ILogger
    {
        private readonly Microsoft.Extensions.Logging.ILogger _logger;
        private readonly string _fileName;

        internal NuGetLogger(Microsoft.Extensions.Logging.ILogger logger, string fileName)
        {
            ArgumentNullException.ThrowIfNull(logger, nameof(logger));
            ArgumentException.ThrowIfNullOrEmpty(fileName, nameof(fileName));

            _logger = logger;
            _fileName = fileName;
        }

        public void Log(LogLevel level, string data)
        {
            _logger.Log(ConvertLevel(level), $"NuGet [{_fileName}]: {data}");
        }

        public void Log(ILogMessage message)
        {
            Log(message.Level, message.FormatWithCode());
        }

        public Task LogAsync(LogLevel level, string data)
        {
            Log(level, data);

            return Task.CompletedTask;
        }

        public Task LogAsync(ILogMessage message)
        {
            Log(message.Level, message.FormatWithCode());

            return Task.CompletedTask;
        }

        public void LogDebug(string data)
        {
            Log(LogLevel.Debug, data);
        }

        public void LogError(string data)
        {
            Log(LogLevel.Error, data);
        }

        public void LogInformation(string data)
        {
            Log(LogLevel.Information, data);
        }

        public void LogInformationSummary(string data)
        {
            Log(LogLevel.Information, data);
        }

        public void LogMinimal(string data)
        {
            Log(LogLevel.Minimal, data);
        }

        public void LogVerbose(string data)
        {
            Log(LogLevel.Verbose, data);
        }

        public void LogWarning(string data)
        {
            Log(LogLevel.Warning, data);
        }

        private static Microsoft.Extensions.Logging.LogLevel ConvertLevel(LogLevel level)
        {
            return level switch
            {
                LogLevel.Debug => Microsoft.Extensions.Logging.LogLevel.Debug,
                LogLevel.Verbose => Microsoft.Extensions.Logging.LogLevel.Trace,
                LogLevel.Information => Microsoft.Extensions.Logging.LogLevel.Information,
                LogLevel.Minimal => Microsoft.Extensions.Logging.LogLevel.Information,
                LogLevel.Warning => Microsoft.Extensions.Logging.LogLevel.Warning,
                LogLevel.Error => Microsoft.Extensions.Logging.LogLevel.Error,
                _ => Microsoft.Extensions.Logging.LogLevel.Information
            };
        }
    }
}

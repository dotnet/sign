using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using SignService.Services;

namespace SignService.Utils
{
    public interface ITelemetryLogger
    {
        void OnSignFile(string file, string toolName);
        void TrackSignToolDependency(string signTool, string fileName, DateTimeOffset startTime, TimeSpan duration, string redactedArgs, int resultCode);
    }

    public class TelemetryLogger : ITelemetryLogger
    {
        public TelemetryLogger(TelemetryClient telemetryClient, IFileNameService fileNameService)
        {
            this.telemetryClient = telemetryClient;
            this.fileNameService = fileNameService;
        }

        readonly TelemetryClient telemetryClient;
        readonly IFileNameService fileNameService;
        static readonly int StartIndexOfTemp = Path.GetTempPath().LastIndexOf(Path.DirectorySeparatorChar) + 1;

        public void OnSignFile(string file, string toolName)
        {
            var originalFileName = fileNameService.GetFileName(file);

            var evt = new EventTelemetry
            {
                Name = "Sign File",
                Properties =
                {
                    { "FullName", GetRelativeDirectoryUnderTemp(originalFileName) },
                    { "FileName", Path.GetFileName(originalFileName) },
                    { "ToolName", toolName },
                    { "LocalFileName", Path.GetFileName(file) }
                }
            };

            telemetryClient.TrackEvent(evt);
        }

        public void TrackSignToolDependency(string signTool, string fileName, DateTimeOffset startTime, TimeSpan duration, string redactedArgs, int resultCode)
        {
            var originalFileName = fileNameService.GetFileName(fileName);

            var file = GetRelativeDirectoryUnderTemp(originalFileName);

            var depTelemetry = new DependencyTelemetry
            {
                Name = $"SIGN {file}",
                Type = "Sign Tool",
                Data = redactedArgs ?? file,
                ResultCode = resultCode.ToString(CultureInfo.InvariantCulture),
                Timestamp = startTime,
                Target = signTool,
                Duration = duration,                
                Success = resultCode == 0
            };

            telemetryClient.TrackDependency(depTelemetry);
        }

        static string GetRelativeDirectoryUnderTemp(string fileName)
        {
            return fileName?.Substring(fileName.IndexOf(Path.DirectorySeparatorChar, StartIndexOfTemp));
        }
    }
}

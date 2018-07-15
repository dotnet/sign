using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;

namespace SignService.Utils
{
    public interface ITelemetryLogger
    {
        void OnSignFile(string file, string toolName);
        void TrackSignToolDependency(string signTool, string fileName, DateTimeOffset startTime, TimeSpan duration, string redactedArgs, int resultCode);
    }

    public class TelemetryLogger : ITelemetryLogger
    {
        readonly TelemetryClient telemetryClient = new TelemetryClient();
        static readonly int StartIndexOfTemp = Path.GetTempPath().LastIndexOf(Path.DirectorySeparatorChar) + 1;

        public void OnSignFile(string file, string toolName)
        {
            var evt = new EventTelemetry
            {
                Name = "Sign File",
                Properties =
                {
                    { "FullName", GetRelativeDirectoryUnderTemp(file) },
                    { "FileName", Path.GetFileName(file) },
                    { "ToolName", toolName }
                }
            };

            telemetryClient.TrackEvent(evt);
        }

        public void TrackSignToolDependency(string signTool, string fileName, DateTimeOffset startTime, TimeSpan duration, string redactedArgs, int resultCode)
        {
            var file = GetRelativeDirectoryUnderTemp(fileName);

            var depTelemetry = new DependencyTelemetry
            {
                Name = $"SIGN {file}",
                Type = signTool,
                Data = redactedArgs ?? file,
                ResultCode = resultCode.ToString(CultureInfo.InvariantCulture),
                Timestamp = startTime,
                Target = file,
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

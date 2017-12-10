using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;

namespace SignService.Utils
{
    public interface ITelemetryLogger
    {
        void OnSignFile(string file, string toolName);
        void TrackDependency(string commandName, DateTimeOffset startTime, TimeSpan duration, string redactedArgs, int resultCode);
    }

    public class TelemetryLogger : ITelemetryLogger
    {
        readonly TelemetryClient telemetryClient = new TelemetryClient();

        public void OnSignFile(string file, string toolName)
        {
            var evt = new EventTelemetry
            {
                Name = "Sign File",
                Properties =
                {
                    { "FullPath", file },
                    { "FileName", Path.GetFileName(file) },
                    { "Directory", Path.GetDirectoryName(file) },
                    { "ToolName", toolName }
                }
            };
            
            telemetryClient.TrackEvent(evt);
        }

        public void TrackDependency(string commandName, DateTimeOffset startTime, TimeSpan duration, string redactedArgs, int resultCode)
        {

            var depTelemetry = new DependencyTelemetry
            {
                Name = commandName,
                Type = "SignTool",
                Data = redactedArgs,
                ResultCode = resultCode.ToString(CultureInfo.InvariantCulture),
                Timestamp = startTime,
                Duration =  duration,
                Success = resultCode == 0
            };

            telemetryClient.TrackDependency(depTelemetry);
        }
    }
}

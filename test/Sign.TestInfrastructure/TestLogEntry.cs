// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.Logging;

namespace Sign.TestInfrastructure
{
    public sealed class TestLogEntry
    {
        public LogLevel LogLevel { get; }
        public string Message { get; }

        internal TestLogEntry(LogLevel logLevel, string message)
        {
            LogLevel = logLevel;
            Message = message;
        }
    }
}
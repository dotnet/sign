// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Xunit;

namespace Sign.TestInfrastructure
{
    public sealed class RequiresElevationTheoryAttribute : TheoryAttribute
    {
        private const string SkipReason = "Test skipped because it requires elevation.";

        private static readonly Lazy<bool> LazyShouldRun = new(ShouldRun);

        public override string Skip
        {
            get { return LazyShouldRun.Value ? null! : SkipReason; }
            set { base.Skip = value; }
        }

        private static bool ShouldRun()
        {
            // It is assumed that CI runs elevated by default,
            // while dev machines don't.
            return IsCI() || IsElevated();
        }

        private static bool IsCI()
        {
            string? value = Environment.GetEnvironmentVariable("CI");

            if (bool.TryParse(value, out bool boolValue))
            {
                return boolValue;
            }

            return false;
        }

        private static bool IsElevated()
        {
            return Environment.IsPrivilegedProcess;
        }
    }
}

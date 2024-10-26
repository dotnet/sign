// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Xunit;

namespace Sign.TestInfrastructure
{
    public sealed class ElevatedTheoryAttribute : TheoryAttribute
    {
        private const string SkipReason = "Test skipped because it requires elevation.";

        private static readonly Lazy<bool> LazyIsElevated = new(IsElevated);

        public override string Skip
        {
            get { return LazyIsElevated.Value ? null! : SkipReason; }
            set { base.Skip = value; }
        }

        private static bool IsElevated()
        {
            return Environment.IsPrivilegedProcess;
        }
    }
}

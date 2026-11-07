// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Xunit;

namespace Sign.TestInfrastructure
{
    public sealed class RequiresCaseSensitiveDirectoryFactAttribute :
        FactAttribute
    {
        private const string SkipReason =
            "Requires Windows case-sensitive directory support and sufficient privileges.";

        private static readonly Lazy<bool> LazyShouldRun =
            new(CaseSensitiveDirectory.IsSupported);

        public override string Skip
        {
            get { return LazyShouldRun.Value ? null! : SkipReason; }
            set { base.Skip = value; }
        }
    }
}

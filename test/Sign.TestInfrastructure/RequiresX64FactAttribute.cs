// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Runtime.InteropServices;
using Xunit;

namespace Sign.TestInfrastructure
{
    public sealed class RequiresX64FactAttribute : FactAttribute
    {
        public RequiresX64FactAttribute()
        {
            if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
            {
                Skip = "Test skipped because it requires an x64-only native signing plugin.";
            }
        }
    }
}

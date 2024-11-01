// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Runtime.CompilerServices;
using Sign.TestInfrastructure;

namespace Sign.Core.Test
{
    public sealed class AssemblyInitializer
    {
        [ModuleInitializer]
        public static void Initialize()
        {
            AppInitializer.Initialize();

            EphemeralTrust.RemoveResidualTestCertificates();
        }
    }
}

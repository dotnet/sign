// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core.Test
{
    public sealed class TestServerFixture : IDisposable
    {
        internal static ITestServer Instance { get; }

        internal ITestServer Server { get; } = Instance;

        static TestServerFixture()
        {
            Instance = TestServer.CreateAsync().GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            Server.Dispose();
        }
    }
}
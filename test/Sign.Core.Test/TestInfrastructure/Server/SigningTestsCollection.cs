// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core.Test
{
    [CollectionDefinition(Name)]
    public sealed class SigningTestsCollection :
        ICollectionFixture<CertificatesFixture>,
        ICollectionFixture<TestServerFixture>
    {
        internal const string Name = nameof(SigningTestsCollection);

        // This class has no code, and is never created. Its purpose is simply
        // to be the place to apply [CollectionDefinition] and all the
        // ICollectionFixture<> interfaces.
    }
}
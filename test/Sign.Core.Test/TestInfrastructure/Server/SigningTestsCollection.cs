// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Sign.TestInfrastructure;

namespace Sign.Core.Test
{
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class SigningTestsCollection :
        ICollectionFixture<CertificatesFixture>,
        ICollectionFixture<TrustedCertificateFixture>,
        ICollectionFixture<TestServerFixture>,
        ICollectionFixture<PfxFilesFixture>
    {
        internal const string Name = nameof(SigningTestsCollection);

        // This class has no code, and is never created. Its purpose is simply
        // to be the place to apply [CollectionDefinition] and all the
        // ICollectionFixture<> interfaces.
    }
}

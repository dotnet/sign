// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;

namespace Sign.Cli.Test
{
    public class AzureCredentialOptionsTests
    {
        private readonly AzureCredentialOptions _options = new();

        [Fact]
        public void ManagedIdentityOption_Always_HasArityOfZeroOrOne()
        {
            Assert.Equal(ArgumentArity.ZeroOrOne, _options.ManagedIdentityOption.Arity);
        }

        [Fact]
        public void ManagedIdentityOption_Always_IsNotRequired()
        {
            Assert.False(_options.ManagedIdentityOption.IsRequired);
        }

        [Fact]
        public void TenantIdOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _options.TenantIdOption.Arity);
        }

        [Fact]
        public void TenantIdOption_Always_IsNotRequired()
        {
            Assert.False(_options.TenantIdOption.IsRequired);
        }

        [Fact]
        public void ClientIdOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _options.ClientIdOption.Arity);
        }

        [Fact]
        public void ClientIdOption_Always_IsNotRequired()
        {
            Assert.False(_options.ClientIdOption.IsRequired);
        }

        [Fact]
        public void ClientSecretOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _options.ClientSecretOption.Arity);
        }

        [Fact]
        public void ClientSecretOption_Always_IsNotRequired()
        {
            Assert.False(_options.ClientSecretOption.IsRequired);
        }
    }
}

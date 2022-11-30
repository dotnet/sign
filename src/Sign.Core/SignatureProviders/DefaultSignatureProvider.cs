// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.DependencyInjection;

namespace Sign.Core
{
    internal sealed class DefaultSignatureProvider : IDefaultSignatureProvider
    {
        public ISignatureProvider SignatureProvider { get; }

        // Dependency injection requires a public constructor.
        public DefaultSignatureProvider(IServiceProvider serviceProvider)
        {
            ArgumentNullException.ThrowIfNull(serviceProvider, nameof(serviceProvider));

            foreach (ISignatureProvider signatureProvider in serviceProvider.GetServices<ISignatureProvider>())
            {
                if (signatureProvider is IAzureSignToolSignatureProvider)
                {
                    SignatureProvider = signatureProvider;

                    return;
                }
            }

            SignatureProvider = new DoNothingDefaultSignatureProvider();
        }

        public bool CanSign(FileInfo file)
        {
            return SignatureProvider.CanSign(file);
        }

        public Task SignAsync(IEnumerable<FileInfo> files, SignOptions options)
        {
            return SignatureProvider.SignAsync(files, options);
        }

        private sealed class DoNothingDefaultSignatureProvider : ISignatureProvider
        {
            public bool CanSign(FileInfo file)
            {
                return false;
            }

            public Task SignAsync(IEnumerable<FileInfo> files, SignOptions options)
            {
                throw new NotImplementedException();
            }
        }
    }
}
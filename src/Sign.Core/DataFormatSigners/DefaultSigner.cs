// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.DependencyInjection;

namespace Sign.Core
{
    internal sealed class DefaultSigner : IDefaultDataFormatSigner
    {
        public IDataFormatSigner Signer { get; }

        // Dependency injection requires a public constructor.
        public DefaultSigner(IServiceProvider serviceProvider)
        {
            ArgumentNullException.ThrowIfNull(serviceProvider, nameof(serviceProvider));

            foreach (IDataFormatSigner signer in serviceProvider.GetServices<IDataFormatSigner>())
            {
                if (signer is IAzureSignToolDataFormatSigner)
                {
                    Signer = signer;

                    return;
                }
            }

            Signer = new DoNothingDefaultDataFormatSigner();
        }

        public bool CanSign(FileInfo file)
        {
            return Signer.CanSign(file);
        }

        public Task SignAsync(IEnumerable<FileInfo> files, SignOptions options)
        {
            return Signer.SignAsync(files, options);
        }

        private sealed class DoNothingDefaultDataFormatSigner : IDataFormatSigner
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
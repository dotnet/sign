// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Build.Tasks.Deployment.ManifestUtilities;

namespace Sign.Core
{
    internal sealed class DeployManifestAdapter :
        ClickOnceManifestAdapter<DeployManifest>,
        IDeployManifest
    {
        internal DeployManifestAdapter(DeployManifest manifest)
            : this(
                manifest,
                hasUnsupportedResourceFallback: false)
        {
        }

        internal DeployManifestAdapter(
            DeployManifest manifest,
            bool hasUnsupportedResourceFallback)
            : base(manifest)
        {
            HasUnsupportedResourceFallback =
                hasUnsupportedResourceFallback;
        }

        public bool HasUnsupportedResourceFallback { get; }
        public bool MapFileExtensions => Manifest.MapFileExtensions;

        public void ApplySigningOptions(SignOptions options, string defaultPublisher)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(defaultPublisher);
            if (!string.IsNullOrWhiteSpace(options.ApplicationName))
            {
                Manifest.Product = options.ApplicationName;
            }
            Manifest.Publisher = string.IsNullOrEmpty(options.PublisherName)
                ? defaultPublisher : options.PublisherName;
            if (options.DescriptionUrl is not null)
            {
                Manifest.SupportUrl = options.DescriptionUrl.AbsoluteUri;
            }
        }
    }
}

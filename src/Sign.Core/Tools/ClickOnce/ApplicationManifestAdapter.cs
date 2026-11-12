// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Globalization;
using Microsoft.Build.Tasks.Deployment.ManifestUtilities;

namespace Sign.Core
{
    internal sealed class ApplicationManifestAdapter :
        ClickOnceManifestAdapter<ApplicationManifest>,
        IApplicationManifest
    {
        internal ApplicationManifestAdapter(ApplicationManifest manifest)
            : base(manifest)
        {
        }

        public void SynchronizeAssemblyIdentities()
        {
            foreach (AssemblyReference reference in
                ClickOncePayloadResolver.GetPhysicalReferences(this)
                    .OfType<AssemblyReference>())
            {
                try
                {
                    AssemblyIdentity? identity = reference.ReferenceType switch
                    {
                        AssemblyReferenceType.NativeAssembly =>
                            AssemblyIdentity.FromNativeAssembly(reference.ResolvedPath),
                        AssemblyReferenceType.ClickOnceManifest =>
                            AssemblyIdentity.FromManifest(reference.ResolvedPath),
                        _ => AssemblyIdentity.FromManagedAssembly(reference.ResolvedPath) ??
                            AssemblyIdentity.FromNativeAssembly(reference.ResolvedPath)
                    };

                    if (identity is not null)
                    {
                        reference.AssemblyIdentity = identity;
                    }
                    // Native PE files need not embed an assembly manifest.
                    // Their publisher-supplied identity has no on-disk replacement.
                    else if (reference.ReferenceType != AssemblyReferenceType.NativeAssembly ||
                        reference.AssemblyIdentity is null)
                    {
                        throw new SigningException(string.Format(
                            CultureInfo.CurrentCulture,
                            Resources.ClickOnceAssemblyIdentityReadFailed,
                            reference.ResolvedPath));
                    }
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or
                    BadImageFormatException or ArgumentException)
                {
                    throw new SigningException(string.Format(
                        CultureInfo.CurrentCulture,
                        Resources.ClickOnceAssemblyIdentityReadFailed,
                        reference.ResolvedPath), exception);
                }
            }
        }
    }
}

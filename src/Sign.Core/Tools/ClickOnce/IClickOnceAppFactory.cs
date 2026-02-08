// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Sign.Core
{
    internal interface IClickOnceAppFactory
    {
        bool TryReadFromDeploymentManifest(
            FileInfo deploymentManifestFile,
            ILogger logger,
            [NotNullWhen(true)] out IClickOnceApp? clickOnceApp);
    }
}
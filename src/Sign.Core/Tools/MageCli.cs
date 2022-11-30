// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.Logging;

namespace Sign.Core
{
    internal sealed class MageCli : CliTool, IMageCli
    {
        // Dependency injection requires a public constructor.
        public MageCli(IToolConfigurationProvider toolConfigurationProvider, ILogger<IMageCli> logger)
            : base(toolConfigurationProvider.Mage, logger)
        {
        }
    }
}
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.Logging;

namespace Sign.Core
{
    internal abstract class Tool
    {
        protected ILogger Logger { get; }

        internal Tool(ILogger<ITool> logger)
        {
            ArgumentNullException.ThrowIfNull(logger, nameof(logger));

            Logger = logger;
        }
    }
}
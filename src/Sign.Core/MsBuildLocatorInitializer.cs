// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Build.Locator;

namespace Sign.Core
{
    internal static class MsBuildLocatorInitializer
    {
        private static readonly object _lock = new();
        private static bool _initialized;

        internal static void EnsureRegistered()
        {
            if (_initialized)
            {
                return;
            }

            lock (_lock)
            {
                if (_initialized || MSBuildLocator.IsRegistered)
                {
                    _initialized = true;
                    return;
                }

                MSBuildLocator.RegisterDefaults();
                _initialized = true;
            }
        }
    }
}

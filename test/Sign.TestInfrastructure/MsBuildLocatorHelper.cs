// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Diagnostics;
using Microsoft.Build.Locator;

namespace Sign.TestInfrastructure
{
    /// <summary>
    /// Thread-safe helper for initializing MSBuildLocator in test environments.
    /// </summary>
    internal static class MsBuildLocatorHelper
    {
        private static bool _initialized;
        private static readonly object _lock = new();

        /// <summary>
        /// Ensures MSBuildLocator is registered exactly once across all test classes.
        /// Uses RegisterDefaults() first, then falls back to vswhere if that fails.
        /// </summary>
        public static void EnsureInitialized()
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

                try
                {
                    MSBuildLocator.RegisterDefaults();
                    _initialized = true;
                }
                catch (InvalidOperationException)
                {
                    // MSBuildLocator.RegisterDefaults() can fail in some test environments
                    // Try to manually locate and register MSBuild using vswhere
                    try
                    {
                        FileInfo msbuildExe = FindMSBuildUsingVsWhereAsync().GetAwaiter().GetResult();
                        string msbuildDirectory = msbuildExe.Directory!.FullName;
                        MSBuildLocator.RegisterMSBuildPath(msbuildDirectory);
                        _initialized = true;
                    }
                    catch
                    {
                        // If we can't register MSBuild, tests that use MSBuild APIs will fail
                        // with clearer error messages when they attempt to use MSBuild functionality
                        _initialized = true;
                    }
                }
            }
        }

        /// <summary>
        /// Locates MSBuild.exe using vswhere utility.
        /// </summary>
        /// <returns>FileInfo for MSBuild.exe</returns>
        /// <exception cref="InvalidOperationException">If vswhere fails to locate MSBuild</exception>
        /// <exception cref="FileNotFoundException">If MSBuild.exe is not found at the expected path</exception>
        public static async Task<FileInfo> FindMSBuildUsingVsWhereAsync()
        {
            FileInfo vsWhereExe = TestTools.GetVsWhereExe();
            ProcessStartInfo vsWherePsi = new()
            {
                FileName = vsWhereExe.FullName,
                Arguments = "-latest -prerelease -products * -requires Microsoft.Component.MSBuild -find MSBuild\\**\\Bin\\MSBuild.exe",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using Process vsWhereProcess = Process.Start(vsWherePsi)!;
            string output = await vsWhereProcess.StandardOutput.ReadToEndAsync();
            await vsWhereProcess.WaitForExitAsync();

            if (vsWhereProcess.ExitCode != 0)
            {
                throw new InvalidOperationException($"vswhere exited with code {vsWhereProcess.ExitCode}");
            }

            string msbuildPath = output.Trim().Split(Environment.NewLine).FirstOrDefault()
                ?? throw new InvalidOperationException("vswhere did not return any MSBuild paths");

            FileInfo msbuildExe = new(msbuildPath);

            if (!msbuildExe.Exists)
            {
                throw new FileNotFoundException($"MSBuild.exe not found at {msbuildPath}");
            }

            return msbuildExe;
        }
    }
}

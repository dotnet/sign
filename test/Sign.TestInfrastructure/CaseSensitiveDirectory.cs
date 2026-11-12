// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.ComponentModel;
using System.Diagnostics;

namespace Sign.TestInfrastructure
{
    public static class CaseSensitiveDirectory
    {
        public static bool TryEnable(DirectoryInfo directory)
        {
            ArgumentNullException.ThrowIfNull(
                directory,
                nameof(directory));

            ProcessStartInfo start = new("fsutil.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            foreach (
                string argument
                in new[]
                {
                    "file",
                    "setCaseSensitiveInfo",
                    directory.FullName,
                    "enable"
                })
            {
                start.ArgumentList.Add(argument);
            }

            using Process? process = Process.Start(start);

            if (process is null)
            {
                return false;
            }

            if (!process.WaitForExit(milliseconds: 10_000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();

                return false;
            }

            return process.ExitCode == 0;
        }

        internal static bool IsSupported()
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            string path = Path.Combine(
                Path.GetTempPath(),
                Path.GetRandomFileName());

            try
            {
                DirectoryInfo directory = Directory.CreateDirectory(path);

                if (!TryEnable(directory))
                {
                    return false;
                }

                File.WriteAllText(
                    Path.Combine(path, "Probe"),
                    "upper");
                File.WriteAllText(
                    Path.Combine(path, "probe"),
                    "lower");

                return directory.EnumerateFiles().Count() == 2;
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                Win32Exception or
                InvalidOperationException)
            {
                return false;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(path))
                    {
                        Directory.Delete(path, recursive: true);
                    }
                }
                catch (Exception exception) when (
                    exception is IOException or
                    UnauthorizedAccessException)
                {
                }
            }
        }
    }
}

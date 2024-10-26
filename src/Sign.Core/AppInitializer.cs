// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal static class AppInitializer
    {
        internal static void Initialize()
        {
            AppRootDirectoryLocator locator = new();
            DirectoryInfo appRootDirectory = locator.Directory;

            string baseDirectory = Path.Combine(appRootDirectory.FullName, "tools", "SDK", "x64");

            //
            // Ensure we invoke wintrust!DllMain before we get too far.
            // This will call wintrust!RegisterSipsFromIniFile and read in wintrust.dll.ini
            // to swap out some local SIPs. Internally, wintrust will call LoadLibraryW
            // on each DLL= entry, so we need to also adjust our DLL search path or we'll
            // load unwanted system-provided copies.
            //
            Kernel32.SetDllDirectoryW(baseDirectory);
            Kernel32.LoadLibraryW(Path.Combine(baseDirectory, "wintrust.dll"));
            Kernel32.LoadLibraryW(Path.Combine(baseDirectory, "mssign32.dll"));

            // This is here because we need to P/Invoke into clr.dll for _AxlPublicKeyBlobToPublicKeyToken.
            string windir = Environment.GetEnvironmentVariable("windir")!;
            string netfxDir = Path.Combine(windir, "Microsoft.NET", "Framework64", "v4.0.30319");

            AddEnvironmentPath(netfxDir);
        }

        private static void AddEnvironmentPath(string path)
        {
            const string name = "PATH";

            string paths = Environment.GetEnvironmentVariable(name) ?? string.Empty;
            string newPaths = string.Join(Path.PathSeparator, paths, path);

            Environment.SetEnvironmentVariable(name, newPaths);
        }
    }
}

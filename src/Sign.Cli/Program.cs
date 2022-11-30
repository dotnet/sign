// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using Sign.Core;

namespace Sign.Cli
{
    internal static class Program
    {
        internal static async Task<int> Main(string[] args)
        {
            if (!Environment.Is64BitProcess)
            {
                Console.Error.WriteLine("Only Windows x64 is supported at this time. See https://github.com/dotnet/sign/issues/474 regarding Windows x86 support.");

                return ExitCode.Failed;
            }

            string directory = Path.GetDirectoryName(Environment.ProcessPath!)!;
            string baseDirectory = Path.Combine(directory, "tools", "SDK", "x64");

            //
            // Ensure we invoke wintrust!DllMain before we get too far.
            // This will call wintrust!RegisterSipsFromIniFile and read in wintrust.dll.ini
            // to swap out some local SIPs. Internally, wintrust will call LoadLibraryW
            // on each DLL= entry, so we need to also adjust our DLL search path or we'll
            // load unwanted system-provided copies.
            //
            Kernel32.SetDllDirectoryW(baseDirectory);
            Kernel32.LoadLibraryW($@"{baseDirectory}\wintrust.dll");
            Kernel32.LoadLibraryW($@"{baseDirectory}\mssign32.dll");

            // This is here because we need to P/Invoke into clr.dll for _AxlPublicKeyBlobToPublicKeyToken             
            string windir = Environment.GetEnvironmentVariable("windir")!;
            string netfxDir = $@"{windir}\Microsoft.NET\Framework64\v4.0.30319";

            AddEnvironmentPath(netfxDir);

            try
            {
                Parser parser = CreateParser();

                return await parser.InvokeAsync(args);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);

                return ExitCode.Failed;
            }
        }

        internal static Parser CreateParser(IServiceProvider? serviceProvider = null)
        {
            SignCommand command = new();

            return new CommandLineBuilder(command)
                .UseVersionOption()
                .UseParseErrorReporting()
                .UseHelp()
                .Build();
        }

        private static void AddEnvironmentPath(string path)
        {
            string paths = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            string newPaths = string.Join(Path.PathSeparator.ToString(), paths, path);

            Environment.SetEnvironmentVariable("PATH", newPaths);
        }
    }
}
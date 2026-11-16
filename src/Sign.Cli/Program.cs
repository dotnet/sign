// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Runtime.InteropServices;
using Sign.Core;

namespace Sign.Cli
{
    internal static class Program
    {
        internal static async Task<int> Main(string[] args)
        {
            using (new TemporaryConsoleEncoding())
            {
                if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64))
                {
                    Console.Error.WriteLine(Resources.x86NotSupported);

                    return ExitCode.Failed;
                }

                AppInitializer.Initialize();

                string systemDirectoryPath = Environment.GetFolderPath(Environment.SpecialFolder.System);

                // NavSip.dll has a dependency on this.
                string vcRuntime140FilePath = Path.Combine(systemDirectoryPath, "vcruntime140.dll");

                if (RuntimeInformation.ProcessArchitecture == Architecture.X64 && !File.Exists(vcRuntime140FilePath))
                {
                    WriteWarning(Resources.MsvcrtNotDetected);
                }

                try
                {
                    SignCommand rootCommand = CreateCommand(serviceProviderFactory: null);

                    return await rootCommand.Parse(args).InvokeAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);

                    return ExitCode.Failed;
                }
            }
        }

        private static void WriteWarning(string warning)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(warning);
            Console.ResetColor();
        }

        internal static SignCommand CreateCommand(IServiceProviderFactory? serviceProviderFactory = null)
        {
            return new SignCommand(serviceProviderFactory);
        }
    }
}

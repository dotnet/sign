// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System;
using System.Runtime.InteropServices;

namespace Sign.Validation
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length != 1 ||
                !Enum.TryParse(args[0], ignoreCase: true, out Architecture expected) ||
                expected is not (Architecture.X64 or Architecture.Arm64))
            {
                Console.Error.WriteLine("Expected one architecture argument: X64 or Arm64.");
                return 1;
            }

            Console.WriteLine($"Expected architecture: {expected}");
            Console.WriteLine($"Process architecture: {RuntimeInformation.ProcessArchitecture}");
            Console.WriteLine($"OS architecture: {RuntimeInformation.OSArchitecture}");
            Console.WriteLine($"Framework: {RuntimeInformation.FrameworkDescription}");

            if (RuntimeInformation.ProcessArchitecture != expected ||
                RuntimeInformation.OSArchitecture != expected)
            {
                Console.Error.WriteLine($"Expected native {expected} execution.");
                return 1;
            }

            Console.WriteLine("Infrastructure validation passed. Sign product build and signing checks were not run.");
            return 0;
        }
    }
}

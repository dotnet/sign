// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;
using System.CommandLine.IO;
using System.Globalization;

namespace Sign.Cli
{
    internal static class StandardStreamWriterExtensions
    {
        internal static void WriteFormattedLine(this IStandardStreamWriter writer, string format, params IdentifierSymbol[] symbols)
        {
            string[] formattedSymbols = symbols
                .Select(symbol => $"--{symbol.Name}")
                .ToArray();

            writer.WriteLine(string.Format(CultureInfo.CurrentCulture, format, formattedSymbols));
        }
    }
}

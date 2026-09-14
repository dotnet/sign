// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal static class OpcPartNameValidator
    {
        private static readonly char[] UnsupportedUriDelimiters = ['#', '?'];

        /// <summary>
        /// Rejects raw URI delimiters before a ZIP entry name is interpreted as a relative package URI.
        /// This is intentionally not a complete OPC part-name validator.
        /// </summary>
        internal static void ThrowIfContainsUnsupportedUriDelimiter(string partName)
        {
            int index = partName.IndexOfAny(UnsupportedUriDelimiters);

            if (index >= 0)
            {
                throw new InvalidDataException(
                    string.Format(
                        Resources.VSIXSignToolOpcPartNameContainsUnsupportedUriDelimiter,
                        partName,
                        partName[index]));
            }
        }
    }
}

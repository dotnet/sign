// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal static class HexHelpers
    {
        private static ReadOnlySpan<byte> LookupTable => new byte[]
        {
            (byte)'0', (byte)'1', (byte)'2', (byte)'3', (byte)'4',
            (byte)'5', (byte)'6', (byte)'7', (byte)'8', (byte)'9',
            (byte)'A', (byte)'B', (byte)'C', (byte)'D', (byte)'E',
            (byte)'F',
        };

        internal static bool IsHex(ReadOnlySpan<char> text)
        {
            if (text.IsEmpty)
            {
                return false;
            }

            for (var i = 0; i < text.Length; ++i)
            {
                char c = text[i];

                if (!char.IsDigit(c) && !(c >= 'a' && c <= 'f') && !(c >= 'A' && c <= 'F'))
                {
                    return false;
                }
            }

            return true;
        }

        public static bool TryHexEncode(ReadOnlySpan<byte> data, Span<char> buffer)
        {
            var charsRequired = data.Length * 2;
            if (buffer.Length < charsRequired)
            {
                return false;
            }
            for (int i = 0, j = 0; i < data.Length; i++, j += 2)
            {
                var value = data[i];
                buffer[j] = (char)LookupTable[(value & 0xF0) >> 4];
                buffer[j+1] = (char)LookupTable[value & 0x0F];
            }

            return true;
        }
    }
}

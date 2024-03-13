// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core.Test
{
    public class HexHelperTests
    {
        [Theory]
        [InlineData(new byte[] { }, "")]
        [InlineData(new byte[] { 0 }, "00")]
        [InlineData(new byte[] { 0, 0, 0, 1 }, "00000001")]
        [InlineData(new byte[] { 0, 255, 1, 254 }, "00FF01FE")]
        public void ShouldEncodeToHex(byte[] input, string expected)
        {
            Span<char> buffer = stackalloc char[expected.Length];
            Assert.True(HexHelpers.TryHexEncode(input, buffer));
            Assert.Equal(expected, buffer.ToString());
        }

        [Fact]
        public void ShouldReturnFalseIfBufferIsTooSmall()
        {
            Span<char> buffer = stackalloc char[1];
            Assert.False(HexHelpers.TryHexEncode(new byte[2], buffer));
        }

        [Fact]
        public void ShouldNotClobberSurroundingData()
        {
            Span<char> buffer = stackalloc char[] { 'Q', 'Q', 'Q', 'Q' };
            Assert.True(HexHelpers.TryHexEncode(new byte[] { 0x66 }, buffer.Slice(1, 2)));
            Assert.Equal("Q66Q", buffer.ToString());
        }

        [Fact]
        public void ShouldTranslateAllValues()
        {
            Span<char> buffer = stackalloc char[2];
            Span<byte> value = stackalloc byte[1];
            for (var i = 0; i <= 0xFF; i++)
            {
                value[0] = (byte)i;
                Assert.True(HexHelpers.TryHexEncode(value, buffer));
                Assert.Equal(i.ToString("X2"), buffer.ToString());
            }
        }
    }
}

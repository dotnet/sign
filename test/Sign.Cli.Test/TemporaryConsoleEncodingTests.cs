// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Text;

namespace Sign.Cli.Test
{
    public class TemporaryConsoleEncodingTests : IDisposable
    {
        private readonly Encoding _defaultInputEncoding;
        private readonly Encoding _defaultOutputEncoding;

        public TemporaryConsoleEncodingTests()
        {
            _defaultInputEncoding = Console.InputEncoding;
            _defaultOutputEncoding = Console.OutputEncoding;

            Console.InputEncoding = Encoding.UTF8;
            Console.OutputEncoding = Encoding.UTF8;
        }

        public void Dispose()
        {
            Console.InputEncoding = _defaultInputEncoding;
            Console.OutputEncoding = _defaultOutputEncoding;

            GC.SuppressFinalize(this);
        }

        [Fact]
        public void Constructor_Always_SetsUtf8Encoding()
        {
            Console.InputEncoding = Encoding.ASCII;
            Console.OutputEncoding = Encoding.ASCII;

            using (new TemporaryConsoleEncoding())
            {
                Assert.Equal(Encoding.UTF8, Console.InputEncoding);
                Assert.Equal(Encoding.UTF8, Console.OutputEncoding);
            }
        }

        [Fact]
        public void Dispose_Always_RevertsEncoding()
        {
            Console.InputEncoding = Encoding.ASCII;
            Console.OutputEncoding = Encoding.ASCII;

            using (new TemporaryConsoleEncoding())
            {
            }

            Assert.Equal(Encoding.ASCII, Console.InputEncoding);
            Assert.Equal(Encoding.ASCII, Console.OutputEncoding);
        }
    }
}

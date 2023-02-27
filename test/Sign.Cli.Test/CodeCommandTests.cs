// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;

namespace Sign.Cli.Test
{
    public class CodeCommandTests
    {
        private readonly CodeCommand _command = new();

        [Fact]
        public void BaseDirectoryOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _command.BaseDirectoryOption.Arity);
        }

        [Fact]
        public void BaseDirectoryOption_Always_IsNotRequired()
        {
            Assert.False(_command.BaseDirectoryOption.IsRequired);
        }

        [Fact]
        public void DescriptionOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _command.DescriptionOption.Arity);
        }

        [Fact]
        public void DescriptionOption_Always_IsRequired()
        {
            Assert.True(_command.DescriptionOption.IsRequired);
        }

        [Fact]
        public void DescriptionUrlOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _command.DescriptionUrlOption.Arity);
        }

        [Fact]
        public void DescriptionUrlOption_Always_IsRequired()
        {
            Assert.True(_command.DescriptionUrlOption.IsRequired);
        }

        [Fact]
        public void FileDigestOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _command.FileDigestOption.Arity);
        }

        [Fact]
        public void FileDigestOption_Always_IsNotRequired()
        {
            Assert.False(_command.FileDigestOption.IsRequired);
        }

        [Fact]
        public void FileListOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _command.FileListOption.Arity);
        }

        [Fact]
        public void FileListOption_Always_IsNotRequired()
        {
            Assert.False(_command.FileListOption.IsRequired);
        }

        [Fact]
        public void MaxConcurrencyOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _command.MaxConcurrencyOption.Arity);
        }

        [Fact]
        public void MaxConcurrencyOption_Always_IsNotRequired()
        {
            Assert.False(_command.MaxConcurrencyOption.IsRequired);
        }

        [Fact]
        public void OutputOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _command.OutputOption.Arity);
        }

        [Fact]
        public void OutputOption_Always_IsNotRequired()
        {
            Assert.False(_command.OutputOption.IsRequired);
        }

        [Fact]
        public void PublisherNameOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _command.PublisherNameOption.Arity);
        }

        [Fact]
        public void PublisherNameOption_Always_IsNotRequired()
        {
            Assert.False(_command.PublisherNameOption.IsRequired);
        }

        [Fact]
        public void TimestampDigestOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _command.TimestampDigestOption.Arity);
        }

        [Fact]
        public void TimestampDigestOption_Always_IsNotRequired()
        {
            Assert.False(_command.TimestampDigestOption.IsRequired);
        }

        [Fact]
        public void TimestampUrlOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _command.TimestampUrlOption.Arity);
        }

        [Fact]
        public void TimestampUrlOption_Always_IsNotRequired()
        {
            Assert.False(_command.TimestampUrlOption.IsRequired);
        }

        [Fact]
        public void VerbosityOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _command.VerbosityOption.Arity);
        }

        [Fact]
        public void VerbosityOption_Always_IsNotRequired()
        {
            Assert.False(_command.VerbosityOption.IsRequired);
        }
    }
}
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;

namespace Sign.Core.Test
{
    public partial class AggregatingSignerTests
    {
        [Fact]
        public async Task SignAsync_WhenFileAlreadySigned_SkipsFile()
        {
            string[] files = new[] { "a.dll" };
            AggregatingSignerTest test = new(files);
            SignedFileTracker tracker = new();

            foreach (FileInfo file in test.Files)
            {
                tracker.MarkAsSigned(file);
            }

            SignOptions options = new(HashAlgorithmName.SHA256, _timestampService, tracker);

            await test.Signer.SignAsync(test.Files, options);

            Assert.Empty(test.SignerSpy.SignedFiles);
        }

        [Fact]
        public async Task SignAsync_WhenSomeFilesAlreadySigned_SignsOnlyUnsignedFiles()
        {
            string[] files = new[] { "a.dll", "b.dll" };
            AggregatingSignerTest test = new(files);

            SignedFileTracker tracker = new();
            tracker.MarkAsSigned(test.Files.First());

            SignOptions options = new(HashAlgorithmName.SHA256, _timestampService, tracker);

            await test.Signer.SignAsync(test.Files, options);

            Assert.Single(test.SignerSpy.SignedFiles);
            Assert.Equal("b.dll", test.SignerSpy.SignedFiles[0].Name);
        }

        [Fact]
        public async Task SignAsync_AfterSigning_MarksFilesAsSigned()
        {
            string[] files = new[] { "a.dll", "b.dll" };
            AggregatingSignerTest test = new(files);

            SignedFileTracker tracker = new();
            SignOptions options = new(HashAlgorithmName.SHA256, _timestampService, tracker);

            await test.Signer.SignAsync(test.Files, options);

            foreach (FileInfo file in test.Files)
            {
                Assert.True(tracker.HasSigned(file));
            }
        }

        [Fact]
        public async Task SignAsync_WhenCalledTwiceWithSameFiles_SignsFilesOnlyOnce()
        {
            string[] files = new[] { "a.dll", "b.dll" };
            AggregatingSignerTest test = new(files);

            SignedFileTracker tracker = new();
            SignOptions options = new(HashAlgorithmName.SHA256, _timestampService, tracker);

            await test.Signer.SignAsync(test.Files, options);

            Assert.Equal(2, test.SignerSpy.SignedFiles.Count);

            await test.Signer.SignAsync(test.Files, options);

            // No new files should have been signed on the second call.
            Assert.Equal(2, test.SignerSpy.SignedFiles.Count);
        }

        [Fact]
        public async Task SignAsync_WhenAllFilesAlreadySigned_SignsNothing()
        {
            string[] files = new[] { "a.dll", "b.dll", "c.dll" };
            AggregatingSignerTest test = new(files);

            SignedFileTracker tracker = new();

            foreach (FileInfo file in test.Files)
            {
                tracker.MarkAsSigned(file);
            }

            SignOptions options = new(HashAlgorithmName.SHA256, _timestampService, tracker);

            await test.Signer.SignAsync(test.Files, options);

            Assert.Empty(test.SignerSpy.SignedFiles);
        }
    }
}

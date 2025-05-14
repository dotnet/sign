// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Text;
using Microsoft.Extensions.Logging;
using Moq;
using WixToolset.Dtf.Compression.Cab;

namespace Sign.Core.Test
{
    public class CabContainerTests
    {
        [Fact]
        public async Task OpenAsync_ExtractsCabToDirectory()
        {
            string[] expectedFileNames = [".a", "b", "c.d"];
            FileInfo cabFile = CreateCabFile(expectedFileNames);

            using (DirectoryServiceStub directoryService = new())
            using (CabContainer container = new(cabFile, directoryService, Mock.Of<IFileMatcher>(), Mock.Of<ILogger>()))
            {
                await container.OpenAsync();

                FileInfo[] actualFiles = directoryService.Directories[0].GetFiles("*", SearchOption.AllDirectories);
                string[] actualFileNames = actualFiles
                    .Select(file => file.FullName.Substring(directoryService.Directories[0].FullName.Length + 1))
                    .ToArray();

                Assert.Equal(expectedFileNames, actualFileNames);
            }
        }

        [Fact]
        public async Task SaveAsync_CompressesCabFromDirectory()
        {
            string[] fileNames = ["a"];
            FileInfo cabFile = CreateCabFile(fileNames);

            using (DirectoryServiceStub directoryService = new())
            using (CabContainer container = new(cabFile, directoryService, Mock.Of<IFileMatcher>(), Mock.Of<ILogger>()))
            {
                await container.OpenAsync();

                File.WriteAllText(Path.Combine(directoryService.Directories[0].FullName, "b"), "b");

                await container.SaveAsync();
            }

            var cab = new CabInfo(cabFile.FullName);
            var files = cab.GetFiles();
            Assert.Equal(2, files.Count);
            Assert.Contains(files, e => e.Name == "a");
            Assert.Contains(files, e => e.Name == "b");
        }

        private static FileInfo CreateCabFile(params string[] entryNames)
        {
            FileInfo file = new(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));

            var cab = new CabInfo(file.FullName);

            var sourceFiles = new List<string>();
            foreach (string entryName in entryNames)
            {
                var sourceFile = Path.GetTempFileName();
                File.WriteAllBytes(sourceFile, Encoding.UTF8.GetBytes(entryName));

                sourceFiles.Add(sourceFile);
            }

            cab.PackFiles(sourceDirectory: null, sourceFiles, entryNames);

            return file;
        }
    }
}

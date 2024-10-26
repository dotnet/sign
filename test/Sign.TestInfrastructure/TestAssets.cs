// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Reflection;

namespace Sign.TestInfrastructure
{
    public static class TestAssets
    {
        public static FileInfo GetTestAsset(DirectoryInfo destinationDirectory, params string[] fileParts)
        {
            FileInfo thisAssemblyFile = new(Path.Combine(Assembly.GetExecutingAssembly().Location));
            string sourceFilePath = Path.Combine([thisAssemblyFile.DirectoryName!, "TestAssets", .. fileParts]);
            FileInfo sourceFile = new(sourceFilePath);
            string destinationFilePath = Path.Combine([destinationDirectory.FullName, .. fileParts]);
            FileInfo destinationFile = new(destinationFilePath);

            destinationFile.Directory!.Create();

            File.Copy(sourceFile.FullName, destinationFile.FullName);

            return destinationFile;
        }
    }
}

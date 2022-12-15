// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core.Test
{
    public class AppRootDirectoryLocatorTests
    {
        [Fact]
        public void Directory_Always_ReturnsDirectoryOfSignCoreDll()
        {
            DirectoryInfo expectedResult = new FileInfo(typeof(AppRootDirectoryLocator).Assembly.Location).Directory!;

            AppRootDirectoryLocator locator = new();
            DirectoryInfo actualResult = locator.Directory;

            Assert.Equal(expectedResult.FullName, actualResult.FullName);
        }
    }
}
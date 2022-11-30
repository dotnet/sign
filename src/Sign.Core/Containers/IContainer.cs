// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.FileSystemGlobbing;

namespace Sign.Core
{
    internal interface IContainer : IDisposable
    {
        IEnumerable<FileInfo> GetFiles();
        IEnumerable<FileInfo> GetFiles(Matcher matcher);

        ValueTask OpenAsync();
        ValueTask SaveAsync();
    }
}
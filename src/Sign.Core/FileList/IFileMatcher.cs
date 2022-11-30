// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace Sign.Core
{
    internal interface IFileMatcher
    {
        IEnumerable<FileInfo> EnumerateMatches(DirectoryInfoBase directory, Matcher matcher);
    }
}
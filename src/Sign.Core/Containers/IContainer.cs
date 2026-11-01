// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.FileSystemGlobbing;

namespace Sign.Core
{
    internal interface IContainer : IDisposable
    {
        IEnumerable<SigningFile> GetFiles(
            SigningSourceIdentity parentIdentity);
        IEnumerable<SigningFile> GetFiles(
            Matcher matcher,
            SigningSourceIdentity parentIdentity);

        ValueTask OpenAsync();
        ValueTask SaveAsync();
    }
}
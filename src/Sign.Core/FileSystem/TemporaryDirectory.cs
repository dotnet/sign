// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal sealed class TemporaryDirectory : ITemporaryDirectory
    {
        private readonly IDirectoryService _directoryService;

        public DirectoryInfo Directory { get; }

        internal TemporaryDirectory(IDirectoryService directoryService)
        {
            ArgumentNullException.ThrowIfNull(directoryService, nameof(directoryService));

            Directory = directoryService.CreateTemporaryDirectory();

            _directoryService = directoryService;
        }

        public void Dispose()
        {
            _directoryService.Delete(Directory);
        }
    }
}
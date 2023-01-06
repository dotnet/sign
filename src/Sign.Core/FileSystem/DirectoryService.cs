// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Sign.Core
{
    internal sealed class DirectoryService : IDirectoryService
    {
        private readonly ILogger _logger;
        private readonly ConcurrentQueue<DirectoryInfo> _temporaryDirectories = new();

        // Dependency injection requires a public constructor.
        public DirectoryService(ILogger<IDirectoryService> logger)
        {
            ArgumentNullException.ThrowIfNull(logger, nameof(logger));

            _logger = logger;
        }

        public DirectoryInfo CreateTemporaryDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

            _logger.LogTrace(Resources.CreatingDirectory, path);

            DirectoryInfo directory = Directory.CreateDirectory(path);

            _temporaryDirectories.Enqueue(directory);

            return directory;
        }

        public void Delete(DirectoryInfo directory)
        {
            ArgumentNullException.ThrowIfNull(directory, nameof(directory));

            directory.Refresh();

            if (!directory.Exists)
            {
                return;
            }

            _logger.LogTrace(Resources.DeletingDirectory, directory.FullName);


            try
            {
                directory.Delete(recursive: true);

            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, Resources.ExceptionWhileDeletingDirectory, directory.FullName);

                return;
            }

            _logger.LogTrace(Resources.DeletedDirectory, directory.FullName);

            // The directory is not guaranteed to be gone since there could be
            // other open handles. Wait, up to half a second, until the directory is gone.
            for (var i = 0; directory.Exists && i < 5; ++i)
            {
                Thread.Sleep(100);

                directory.Refresh();
            }

            if (directory.Exists)
            {
                _logger.LogTrace(Resources.DirectoryNotDeleted, directory.FullName);
            }
        }

        public void Dispose()
        {
            while (_temporaryDirectories.TryDequeue(out DirectoryInfo? temporaryDirectory))
            {
                Delete(temporaryDirectory);
            }
        }
    }
}
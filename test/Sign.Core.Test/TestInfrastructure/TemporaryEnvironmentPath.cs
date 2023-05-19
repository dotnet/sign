﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core.Test
{
    internal sealed class TemporaryEnvironmentPath : IDisposable
    {
        private const string Name = "PATH";

        private readonly string? _originalEnvironmentPath;

        internal TemporaryEnvironmentPath(string path)
        {
            _originalEnvironmentPath = Environment.GetEnvironmentVariable(Name);

            string paths = _originalEnvironmentPath ?? string.Empty;
            string newPaths = string.Join(Path.PathSeparator.ToString(), paths, path);

            Environment.SetEnvironmentVariable(Name, newPaths);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(Name, _originalEnvironmentPath);

            GC.SuppressFinalize(this);
        }
    }
}
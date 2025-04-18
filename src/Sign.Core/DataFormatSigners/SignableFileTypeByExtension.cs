// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal sealed class SignableFileTypeByExtension : ISignableFileType
    {
        private readonly HashSet<string> _fileExtensions;

        internal SignableFileTypeByExtension(params string[] fileExtensions)
        {
            ArgumentNullException.ThrowIfNull(fileExtensions, nameof(fileExtensions));

            if (fileExtensions.Length == 0)
            {
                throw new ArgumentException(Resources.ArgumentCannotBeEmpty, nameof(fileExtensions));
            }

            _fileExtensions = new HashSet<string>(fileExtensions, StringComparer.OrdinalIgnoreCase);
        }

        public bool IsMatch(FileInfo file)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));

            return _fileExtensions.Contains(file.Extension);
        }
    }
}

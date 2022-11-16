// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Diagnostics.CodeAnalysis;

namespace Sign.Core
{
    internal sealed class FileInfoComparer : IEqualityComparer<FileInfo>
    {
        internal static FileInfoComparer Instance { get; } = new FileInfoComparer();

        public bool Equals(FileInfo? x, FileInfo? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            return string.Equals(x.FullName, y.FullName, StringComparison.Ordinal);
        }

        public int GetHashCode([DisallowNull] FileInfo obj)
        {
            return obj.FullName.GetHashCode();
        }
    }
}
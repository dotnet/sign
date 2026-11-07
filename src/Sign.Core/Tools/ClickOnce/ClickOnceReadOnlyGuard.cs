// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Globalization;

namespace Sign.Core
{
    internal static class ClickOnceReadOnlyGuard
    {
        internal static void ValidateModification(FileInfo source, FileInfo staged)
        {
            if ((File.GetAttributes(source.FullName) & FileAttributes.ReadOnly) != 0 ||
                (File.Exists(staged.FullName) && (File.GetAttributes(staged.FullName) & FileAttributes.ReadOnly) != 0))
            {
                throw new SigningException(string.Format(CultureInfo.CurrentCulture,
                    Resources.ClickOnceReadOnlySource, source.FullName));
            }
        }
    }
}

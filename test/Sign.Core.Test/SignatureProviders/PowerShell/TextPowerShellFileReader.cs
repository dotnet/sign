// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core.Test
{
    internal sealed class TextPowerShellFileReader : PowerShellFileReader
    {
        protected override string StartComment => "#";
        protected override string EndComment => string.Empty;

        internal TextPowerShellFileReader(FileInfo file) : base(file)
        {
        }
    }
}

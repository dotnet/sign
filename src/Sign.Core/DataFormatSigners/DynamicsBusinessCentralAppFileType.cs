// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal sealed class DynamicsBusinessCentralAppFileType : ISignableFileType
    {
        private const string FileExtension = ".app";

        private readonly byte[] _expectedHeader;

        internal DynamicsBusinessCentralAppFileType()
        {
            _expectedHeader = new byte[] { 0x4e, 0x41, 0x56, 0x58 }; // NAVX
        }

        public bool IsMatch(FileInfo file)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));

            if (!FileExtension.Equals(file.Extension, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            using (FileStream stream = file.OpenRead())
            {
                var header = new byte[_expectedHeader.Length];

                if (stream.Read(header, offset: 0, header.Length) != header.Length)
                {
                    return false;
                }

                return header.SequenceEqual(_expectedHeader);
            }
        }
    }
}

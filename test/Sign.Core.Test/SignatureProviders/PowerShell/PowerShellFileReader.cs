// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography.Pkcs;
using System.Text;

namespace Sign.Core.Test
{
    internal abstract class PowerShellFileReader
    {
        private readonly FileInfo _file;

        protected abstract string StartComment { get; }
        protected abstract string EndComment { get; }

        protected PowerShellFileReader(FileInfo file)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));

            _file = file;
        }

        internal static PowerShellFileReader Read(FileInfo file)
        {
            string fileExtension = file.Extension.ToLower();

            return fileExtension switch
            {
                ".psd1" or ".ps1" or ".psm1" => new TextPowerShellFileReader(file),
                ".cdxml" or ".ps1xml" => new XmlPowerShellFileReader(file),
                _ => throw new ArgumentException(message: null, paramName: nameof(file)),
            };
        }

        internal bool TryGetSignature([NotNullWhen(true)] out SignedCms? signedCms)
        {
            signedCms = null;

            try
            {
                if (!TryExtractSignatureBlock(out string? signatureBlock))
                {
                    return false;
                }

                byte[] signatureBytes = Convert.FromBase64String(signatureBlock);
                signedCms = new SignedCms();
                signedCms.Decode(signatureBytes);

                return true;
            }
            catch (Exception)
            {
                signedCms = null;

                return false;
            }
        }

        private bool TryExtractSignatureBlock([NotNullWhen(true)] out string? signatureBlock)
        {
            signatureBlock = null;

            const string sigTag = "SIG #";

            using (FileStream stream = _file.OpenRead())
            using (StreamReader reader = new(stream))
            {
                string? line = null;
                bool signatureBlockFound = false;
                StringBuilder base64 = new();

                while ((line = reader.ReadLine()) is not null)
                {
                    if (!line.StartsWith(StartComment))
                    {
                        continue;
                    }

                    if (!signatureBlockFound)
                    {
                        int startIndex = line.IndexOf(sigTag, StringComparison.OrdinalIgnoreCase);

                        if (startIndex >= 0)
                        {
                            signatureBlockFound = true;
                        }

                        continue;
                    }

                    int endIndex = line.IndexOf(sigTag, StringComparison.OrdinalIgnoreCase);

                    if (endIndex >= 0)
                    {
                        signatureBlock = base64.ToString();

                        return true;
                    }

                    string substring = line;

                    while (true)
                    {
                        substring = substring.Trim();

                        if (!string.IsNullOrEmpty(StartComment) && substring.StartsWith(StartComment))
                        {
                            substring = substring.Substring(StartComment.Length);

                            continue;
                        }

                        if (!string.IsNullOrEmpty(EndComment) && substring.EndsWith(EndComment))
                        {
                            substring = substring.Substring(0, substring.Length - EndComment.Length);

                            continue;
                        }

                        base64.Append(substring);
                        break;
                    }
                }
            }

            return false;
        }
    }
}

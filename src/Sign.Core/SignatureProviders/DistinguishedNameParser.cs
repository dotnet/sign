#pragma warning disable IDE0073 // The file header does not match the required text
// The MIT License (MIT)
// 
// Copyright (c) 2015 Kevin Jones
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System.Runtime.InteropServices;

namespace Sign.Core
{
    // From https://github.com/vcsjones/FiddlerCert/blob/06642751314a9ff224cb37a1cd7c14b86062a119/VCSJones.FiddlerCert/DistinguishedNameParser.cs
    internal static class DistinguishedNameParser
    {
        internal static Dictionary<string, List<string>> Parse(string distingishedName)
        {
            var result = new Dictionary<string, List<string>>(StringComparer.CurrentCultureIgnoreCase);
            var distinguishedNamePtr = IntPtr.Zero;
            try
            {
                distinguishedNamePtr = Marshal.StringToCoTaskMemUni(distingishedName);
                //We need to copy the IntPtr.
                //The copy is necessary because DsGetRdnW modifies the pointer to advance it. We need to keep
                //The original so we can free it later, otherwise we'll leak memory.
                var distinguishedNamePtrCopy = distinguishedNamePtr;
                var pcDN = (uint)distingishedName.Length;
                while (pcDN != 0 && Ntdsapi.DsGetRdnW(ref distinguishedNamePtrCopy, ref pcDN, out var ppKey, out var pcKey, out var ppVal, out var pcVal) == 0)
                {
                    if (pcKey == 0 || pcVal == 0)
                    {
                        continue;
                    }

                    var key = Marshal.PtrToStringUni(ppKey, (int)pcKey);
                    var value = Marshal.PtrToStringUni(ppVal, (int)pcVal);
                    if (result.ContainsKey(key))
                    {
                        result[key].Add(value);
                    }
                    else
                    {
                        result.Add(key, new List<string> { value });
                    }

                    if (pcDN == 0)
                    {
                        break;
                    }
                }

                return result;

            }
            finally
            {
                Marshal.FreeCoTaskMem(distinguishedNamePtr);
            }
        }
    }
}
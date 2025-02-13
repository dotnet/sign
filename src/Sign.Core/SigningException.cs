// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal sealed class SigningException : Exception
    {
        public SigningException()
        {
        }

        public SigningException(string message)
            : base(message)
        {
        }

        public SigningException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}

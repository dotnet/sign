// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core.Timestamp
{
    /// <summary>
    /// Indicates the status of the timestamp operation.
    /// </summary>
    internal enum TimestampResult
    {
        /// <summary>
        /// The timestamp operation was a success.
        /// </summary>
        Success = 1,

        /// <summary>
        /// The package could not be timestamped because it does not have an existing signature.
        /// </summary>
        PackageNotSigned = 2,

        /// <summary>
        /// The timestamp operation failed.
        /// </summary>
        Failed = 3
    }
}

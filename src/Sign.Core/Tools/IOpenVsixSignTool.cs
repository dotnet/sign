// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using OpenVsixSignTool.Core;

namespace Sign.Core
{
    internal interface IOpenVsixSignTool : ITool
    {
        Task<bool> SignAsync(FileInfo file, SignConfigurationSet configuration, SignOptions options);
    }
}
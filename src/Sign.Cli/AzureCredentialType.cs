﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Cli
{
    internal static class AzureCredentialType
    {
        public const string Default = "default";
        public const string AzureCli = "azure-cli";
        public const string Environment = "environment";
        public const string ManagedIdentity = "managed-identity";
    }
}

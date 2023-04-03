#pragma warning disable IDE0073
// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Http;

namespace Sign.Core.Test
{
    internal interface IHttpResponder
    {
        Uri Url { get; }

        Task RespondAsync(HttpContext context);
    }
}
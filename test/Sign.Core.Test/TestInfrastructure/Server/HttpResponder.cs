#pragma warning disable IDE0073
// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Http;

namespace Sign.Core.Test
{
    internal abstract class HttpResponder : IHttpResponder
    {
        public abstract Uri Url { get; }

        public abstract Task RespondAsync(HttpContext context);

        protected static bool IsGet(HttpRequest request)
        {
            return string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase);
        }

        protected static bool IsPost(HttpRequest request)
        {
            return string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase);
        }

        protected static byte[] ReadRequestBody(HttpRequest request)
        {
            if (request.ContentLength is null)
            {
                return Array.Empty<byte>();
            }

            using (BinaryReader reader = new(request.Body))
            {
                return reader.ReadBytes((int)request.ContentLength);
            }
        }

        protected static void WriteResponseBody(HttpResponse response, ReadOnlyMemory<byte> bytes)
        {
            response.ContentLength = bytes.Length;

            using (BinaryWriter writer = new(response.Body))
            {
                writer.Write(bytes.Span);
            }
        }
    }
}
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Sign.Core.Test
{
    internal sealed class AiaResponder : HttpResponder
    {
        private readonly CertificateAuthority _certificateAuthority;
        private readonly TimeSpan? _responseDelay;

        public override Uri Url { get; }

        internal AiaResponder(
            CertificateAuthority certificateAuthority,
            TimeSpan? responseDelay = null)
        {
            ArgumentNullException.ThrowIfNull(certificateAuthority, nameof(certificateAuthority));

            _certificateAuthority = certificateAuthority;
            _responseDelay = responseDelay;

            if (certificateAuthority.AiaHttpUri is null)
            {
                throw new ArgumentException(message: null, nameof(certificateAuthority));
            }

            Url = new Uri(certificateAuthority.AiaHttpUri, UriKind.Absolute);
        }

        public override async Task RespondAsync(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context, nameof(context));

            if (_responseDelay.HasValue)
            {
                Trace.WriteLine($"Delaying response by {_responseDelay.Value}.");

                await Task.Delay(_responseDelay.Value);
            }

            byte[] certData = _certificateAuthority.GetCertData();

            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/pkix-cert";

            await context.Response.Body.WriteAsync(certData);

            Trace.WriteLine($"[AIA]  Responded with {certData.Length}-byte certificate from {_certificateAuthority.SubjectName}.");
        }
    }
}
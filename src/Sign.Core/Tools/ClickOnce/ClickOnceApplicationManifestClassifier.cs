// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Xml;
using Microsoft.Extensions.Logging;

namespace Sign.Core
{
    internal sealed class ClickOnceApplicationManifestClassifier
    {
        private readonly IClickOnceManifestReader _reader;
        private readonly ILogger<IDataFormatSigner> _logger;
        private readonly ConcurrentDictionary<SigningSourceIdentity, Lazy<byte[]?>> _classifications = new();

        internal ClickOnceApplicationManifestClassifier(
            IClickOnceManifestReader reader, ILogger<IDataFormatSigner> logger)
        {
            _reader = reader;
            _logger = logger;
        }

        internal bool IsApplication(SigningFile input) => GetClassification(input) is not null;

        internal void Validate(SigningFile input)
        {
            if (!_classifications.TryGetValue(input.SourceIdentity, out Lazy<byte[]?>? classification) ||
                classification.Value is not { } expected)
            {
                return;
            }
            try
            {
                using FileStream stream = input.File.OpenRead();
                if (!SHA256.HashData(stream).AsSpan().SequenceEqual(expected))
                {
                    throw Changed(input);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw Changed(input, exception);
            }
        }

        private SigningException Changed(SigningFile input, Exception? inner = null)
        {
            string message = string.Format(CultureInfo.CurrentCulture,
                Resources.ClickOnceApplicationManifestChangedAfterClassification, input.File.FullName);
            _logger.LogError("{Message}", message);
            return inner is null ? new SigningException(message) : new SigningException(message, inner);
        }

        private byte[]? GetClassification(SigningFile input) =>
            _classifications.GetOrAdd(input.SourceIdentity, _ => new Lazy<byte[]?>(() =>
            {
                try
                {
                    using FileStream stream = input.File.OpenRead();
                    byte[] hash = SHA256.HashData(stream);
                    stream.Position = 0;
                    bool application = _reader.TryReadApplicationManifest(stream, out IApplicationManifest? manifest);
                    manifest?.Dispose();
                    return application ? hash : null;
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or InvalidOperationException or XmlException)
                {
                    return null;
                }
            })).Value;
    }
}

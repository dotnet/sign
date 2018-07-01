using System;
using System.Collections.Generic;

namespace SignService.Utils
{
    /// <summary>
    /// Handles http bearer challenge operations
    /// </summary>
    public sealed class HttpBearerChallenge
    {
        const string Authorization = "authorization";
        const string AuthorizationUri = "authorization_uri";
        const string Bearer = "Bearer";

        /// <summary>
        /// Tests whether an authentication header is a Bearer challenge
        /// </summary>
        /// <remarks>
        /// This method is forgiving: if the parameter is null, or the scheme
        /// in the header is missing, then it will simply return false.
        /// </remarks>
        /// <param name="challenge">The AuthenticationHeaderValue to test</param>
        /// <returns>True if the header is a Bearer challenge</returns>
        public static bool IsBearerChallenge(string challenge)
        {
            if (string.IsNullOrEmpty(challenge))
            {
                return false;
            }

            if (!challenge.Trim().StartsWith(Bearer + " "))
            {
                return false;
            }

            return true;
        }

        Dictionary<string, string> _parameters = null;

        /// <summary>
        /// Parses an HTTP WWW-Authentication Bearer challenge from a server.
        /// </summary>
        /// <param name="challenge">The AuthenticationHeaderValue to parse</param>
        public HttpBearerChallenge(Uri requestUri, string challenge)
        {
            var authority = ValidateRequestURI(requestUri);
            var trimmedChallenge = ValidateChallenge(challenge);

            SourceAuthority = authority;
            SourceUri = requestUri;

            _parameters = new Dictionary<string, string>();

            // Split the trimmed challenge into a set of name=value strings that
            // are comma separated. The value fields are expected to be within
            // quotation characters that are stripped here.
            var pairs = trimmedChallenge.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries);

            if (pairs != null && pairs.Length > 0)
            {
                // Process the name=value strings
                for (var i = 0; i < pairs.Length; i++)
                {
                    var pair = pairs[i].Split('=');

                    if (pair.Length == 2)
                    {
                        // We have a key and a value, now need to trim and decode
                        var key = pair[0].Trim().Trim(new char[] { '\"' });
                        var value = pair[1].Trim().Trim(new char[] { '\"' });

                        if (!string.IsNullOrEmpty(key))
                        {
                            _parameters[key] = value;
                        }
                    }
                }
            }

            // Minimum set of parameters
            if (_parameters.Count < 1)
            {
                throw new ArgumentException("Invalid challenge parameters", "challenge");
            }

            // Must specify authorization or authorization_uri
            if (!_parameters.ContainsKey(Authorization) && !_parameters.ContainsKey(AuthorizationUri))
            {
                throw new ArgumentException("Invalid challenge parameters", "challenge");
            }
        }

        /// <summary>
        /// Returns the value stored at the specified key.
        /// </summary>
        /// <remarks>
        /// If the key does not exist, will return false and the
        /// content of value will not be changed
        /// </remarks>
        /// <param name="key">The key to be retrieved</param>
        /// <param name="value">The value for the specified key</param>
        /// <returns>True when the key is found, false when it is not</returns>
        public bool TryGetValue(string key, out string value)
        {
            return _parameters.TryGetValue(key, out value);
        }

        /// <summary>
        /// Returns the URI for the Authorization server if present,
        /// otherwise string.Empty
        /// </summary>
        public string AuthorizationServer
        {
            get
            {
                var value = string.Empty;

                if (_parameters.TryGetValue("authorization_uri", out value))
                {
                    return value;
                }

                if (_parameters.TryGetValue("authorization", out value))
                {
                    return value;
                }

                return string.Empty;
            }
        }

        /// <summary>
        /// Returns the Realm value if present, otherwise the Authority
        /// of the request URI given in the ctor
        /// </summary>
        public string Resource
        {
            get
            {
                var value = string.Empty;

                if (_parameters.TryGetValue("resource", out value))
                {
                    return value;
                }

                return SourceAuthority;
            }
        }

        /// <summary>
        /// Returns the Scope value if present, otherwise string.Empty
        /// </summary>
        public string Scope
        {
            get
            {
                var value = string.Empty;

                if (_parameters.TryGetValue("scope", out value))
                {
                    return value;
                }

                return string.Empty;
            }
        }

        /// <summary>
        /// The Authority of the request URI
        /// </summary>
        public string SourceAuthority { get; } = null;

        /// <summary>
        /// The source URI
        /// </summary>
        public Uri SourceUri { get; } = null;

        static string ValidateChallenge(string challenge)
        {
            if (string.IsNullOrEmpty(challenge))
            {
                throw new ArgumentNullException("challenge");
            }

            var trimmedChallenge = challenge.Trim();

            if (!trimmedChallenge.StartsWith(Bearer + " "))
            {
                throw new ArgumentException("Challenge is not Bearer", "challenge");
            }

            return trimmedChallenge.Substring(Bearer.Length + 1);
        }

        static string ValidateRequestURI(Uri requestUri)
        {
            if (null == requestUri)
            {
                throw new ArgumentNullException("requestUri");
            }

            if (!requestUri.IsAbsoluteUri)
            {
                throw new ArgumentException("The requestUri must be an absolute URI", "requestUri");
            }

            if (!requestUri.Scheme.Equals("http", StringComparison.CurrentCultureIgnoreCase) && !requestUri.Scheme.Equals("https", StringComparison.CurrentCultureIgnoreCase))
            {
                throw new ArgumentException("The requestUri must be HTTP or HTTPS", "requestUri");
            }

            return requestUri.FullAuthority();
        }
    }

    static class UriExtensions
    {
        /// <summary>
        /// Returns an authority string for URI that is guaranteed to contain
        /// a port number.
        /// </summary>
        /// <param name="uri">The Uri from which to compute the authority</param>
        /// <returns>The complete authority for the Uri</returns>
        public static string FullAuthority(this Uri uri)
        {
            var authority = uri.Authority;

            if (!authority.Contains(":") && uri.Port > 0)
            {
                // Append port for complete authority
                authority = string.Format("{0}:{1}", uri.Authority, uri.Port.ToString());
            }

            return authority;
        }
    }

}

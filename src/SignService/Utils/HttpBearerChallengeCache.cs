using System;
using System.Collections.Generic;

namespace SignService.Utils
{

    /// <summary>
    /// Singleton class for handling caching of the http bearer challenge
    /// </summary>
    public sealed class HttpBearerChallengeCache
    {
        static readonly HttpBearerChallengeCache _instance = new HttpBearerChallengeCache();

        /// <summary>
        /// Gets the singleton instance of <see cref="HttpBearerChallengeCache"/> 
        /// </summary>
        /// <returns>Instance of this class</returns>
        public static HttpBearerChallengeCache GetInstance()
        {
            return _instance;
        }

        Dictionary<string, HttpBearerChallenge> _cache = null;
        readonly object _cacheLock = null;

        HttpBearerChallengeCache()
        {
            _cache = new Dictionary<string, HttpBearerChallenge>();
            _cacheLock = new object();
        }

        /// <summary>
        /// Gets the challenge for the cached URL.
        /// </summary>
        /// <param name="url"> the URL that the challenge is cached for.</param>
        /// <returns>the cached challenge or null otherwise.</returns>
        public HttpBearerChallenge GetChallengeForURL(Uri url)
        {
            if (url == null)
            {
                throw new ArgumentNullException("url");
            }

            HttpBearerChallenge value = null;

            lock (_cacheLock)
            {
                _cache.TryGetValue(url.FullAuthority(), out value);
            }

            return value;
        }

        /// <summary>
        /// Removes the cached challenge for the specified URL
        /// </summary>
        /// <param name="url"> the URL to remove its cached challenge </param>
        public void RemoveChallengeForURL(Uri url)
        {
            if (url == null)
            {
                throw new ArgumentNullException("url");
            }

            lock (_cacheLock)
            {
                _cache.Remove(url.FullAuthority());
            }
        }

        /// <summary>
        /// Caches the challenge for the specified URL
        /// </summary>
        /// <param name="url"> URL corresponding to challenge as cache key </param>
        /// <param name="value"> the challenge </param>
        public void SetChallengeForURL(Uri url, HttpBearerChallenge value)
        {
            if (url == null)
            {
                throw new ArgumentNullException("url");
            }

            if (value == null)
            {
                throw new ArgumentNullException("value");
            }

            if (string.Compare(url.FullAuthority(), value.SourceAuthority, StringComparison.OrdinalIgnoreCase) != 0)
            {
                throw new ArgumentException("Source URL and Challenge URL do not match");
            }

            lock (_cacheLock)
            {
                _cache[url.FullAuthority()] = value;
            }
        }

        /// <summary>
        /// Clears the cache
        /// </summary>
        public void Clear()
        {
            lock (_cacheLock)
            {
                _cache.Clear();
            }
        }
    }

}

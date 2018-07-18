using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Rest;

namespace SignService.Utils
{

    /// <summary>
    /// The authentication callback delegate which is to be implemented by the client code
    /// </summary>
    /// <param name="authority"> Identifier of the authority, a URL. </param>
    /// <param name="resource"> Identifier of the target resource that is the recipient of the requested token, a URL. </param>
    /// <param name="scope"> The scope of the authentication request. </param>
    /// <returns> access token </returns>
    public delegate Task<string> AutoRestAuthenticationCallback(string authority, string resource, string scope);

    /// <summary>
    /// The credential class that implements <see cref="ServiceClientCredentials"/>
    /// </summary>
    public class AutoRestCredential<T> : ServiceClientCredentials where T : ServiceClient<T>
    {
        ServiceClient<T> _client;

        /// <summary>
        /// The authentication callback
        /// </summary>
        public event AutoRestAuthenticationCallback OnAuthenticate = null;

        /// <summary>
        /// Bearer token
        /// </summary>
        public string Token { get; set; }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="authenticationCallback"> the authentication callback. </param>
        public AutoRestCredential(AutoRestAuthenticationCallback authenticationCallback)
        {
            OnAuthenticate = authenticationCallback;
        }

        /// <summary>
        /// Clones the current AutoRestCredential object.
        /// </summary>
        /// <returns>A new AutoRestCredential instance using the same authentication callback as the current instance.</returns>
        internal AutoRestCredential<T> Clone()
        {
            return new AutoRestCredential<T>(OnAuthenticate);
        }

        async Task<string> PreAuthenticate(Uri url)
        {
            if (OnAuthenticate != null)
            {
                var challenge = HttpBearerChallengeCache.GetInstance().GetChallengeForURL(url);

                if (challenge != null)
                {
                    return await OnAuthenticate(challenge.AuthorizationServer, challenge.Resource, challenge.Scope).ConfigureAwait(false);
                }
                else
                {
                    return await OnAuthenticate(null, null, null).ConfigureAwait(false);
                }
            }

            return null;
        }

        protected async Task<string> PostAuthenticate(HttpResponseMessage response)
        {
            // An HTTP 401 Not Authorized error; handle if an authentication callback has been supplied
            if (OnAuthenticate != null)
            {
                // Extract the WWW-Authenticate header and determine if it represents an OAuth2 Bearer challenge
                var authenticateHeader = response.Headers.WwwAuthenticate.ElementAt(0).ToString();

                if (HttpBearerChallenge.IsBearerChallenge(authenticateHeader))
                {
                    var challenge = new HttpBearerChallenge(response.RequestMessage.RequestUri, authenticateHeader);

                    if (challenge != null)
                    {
                        // Update challenge cache
                        HttpBearerChallengeCache.GetInstance().SetChallengeForURL(response.RequestMessage.RequestUri, challenge);

                        // We have an authentication challenge, use it to get a new authorization token
                        return await OnAuthenticate(challenge.AuthorizationServer, challenge.Resource, challenge.Scope).ConfigureAwait(false);
                    }
                }
            }

            return null;
        }
        public override void InitializeServiceClient<TClient>(ServiceClient<TClient> client)
        {
            base.InitializeServiceClient(client);

            var tClient = client as T;
            _client = tClient ?? throw new ArgumentException($"Credential is only for use with the {typeof(T).Name} service client.");
        }

        public override async Task ProcessHttpRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            var accessToken = await PreAuthenticate(request.RequestUri).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(accessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }
            else
            {
                HttpResponseMessage response;

                // if this credential is tied to a specific TClient reuse it's HttpClient to send the 
                // initial unauthed request to get the challange, otherwise create a new HttpClient
                var client = _client?.HttpClient ?? new HttpClient();

                using (var r = new HttpRequestMessage(request.Method, request.RequestUri))
                {
                    response = await client.SendAsync(r).ConfigureAwait(false);
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    accessToken = await PostAuthenticate(response).ConfigureAwait(false);

                    if (!string.IsNullOrEmpty(accessToken))
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                    }
                }
            }
        }
    }

}

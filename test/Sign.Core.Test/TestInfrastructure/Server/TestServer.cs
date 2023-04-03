// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Sign.Core.Test
{
    internal sealed class TestServer : ITestServer, IStartup, IDisposable
    {
        private readonly ConcurrentDictionary<string, IHttpResponder> _responders;
        private IWebHost? _webHost;
        private Lazy<Uri>? _url;

        public Uri Url => _url!.Value;

        private bool _isDisposed;

        private TestServer()
        {
            _responders = new ConcurrentDictionary<string, IHttpResponder>();
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _webHost?.Dispose();

                GC.SuppressFinalize(this);

                _isDisposed = true;
            }
        }

        public void Configure(IApplicationBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app, nameof(app));

            // The actual port isn't known until later so defer retrieval until it is.
            _url = new Lazy<Uri>(() =>
            {
                IServerAddressesFeature? serverAddressesFeature = app.ServerFeatures.Get<IServerAddressesFeature>();

                if (serverAddressesFeature is null)
                {
                    throw new InvalidOperationException();
                }

                return new Uri(serverAddressesFeature.Addresses.Single());
            });

            app.MapWhen(
                context =>
                {
                    if (context.Request.Path.HasValue)
                    {
                        return _responders.ContainsKey(context.Request.Path.Value);
                    }

                    return false;
                },
                configuration => configuration.Run(async context =>
                {
                    try
                    {
                        await _responders[context.Request.Path.Value].RespondAsync(context);

                        Trace.WriteLine($"Replied:   {context.Request.Path.Value}");
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine(ex);
                    }
                }));
        }

        public IServiceProvider ConfigureServices(IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services, nameof(services));

            return services.BuildServiceProvider();
        }

        public IDisposable RegisterResponder(IHttpResponder responder)
        {
            ArgumentNullException.ThrowIfNull(responder, nameof(responder));

            return new Responder(_responders, responder.Url, responder);
        }

        internal static async Task<ITestServer> CreateAsync()
        {
            TestServer server = new();
            IWebHostBuilder builder = new WebHostBuilder()
                .ConfigureServices(services =>
                {
                    services.AddSingleton<IStartup>(server);
                })
                .UseKestrel()
                .UseUrls("http://127.0.0.1:0"); // automatically pick the port
            IWebHost host = builder.Build();

            server._webHost = host;

            await host.StartAsync(CancellationToken.None);

            Trace.WriteLine($"Test server listening at {server.Url.AbsoluteUri}");

            return server;
        }

        private sealed class Responder : IDisposable
        {
            private readonly ConcurrentDictionary<string, IHttpResponder> _responders;
            private readonly string _key;

            internal Responder(ConcurrentDictionary<string, IHttpResponder> responders, Uri url, IHttpResponder responder)
            {
                _responders = responders;
                _key = url.PathAndQuery;
                _responders[url.PathAndQuery] = responder;
            }

            public void Dispose()
            {
                _responders.TryRemove(_key, out IHttpResponder? _);
            }
        }
    }
}
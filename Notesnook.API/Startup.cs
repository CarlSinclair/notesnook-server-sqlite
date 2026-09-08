/*
This file is part of the Notesnook Sync Server project (https://notesnook.com/)

Copyright (C) 2023 Streetwriters (Private) Limited

This program is free software: you can redistribute it and/or modify
it under the terms of the Affero GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
Affero GNU General Public License for more details.

You should have received a copy of the Affero GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.IdentityModel.Tokens.Jwt;
using System.IO.Compression;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Amazon.Runtime;
using StackExchange.Redis;
using IdentityModel.AspNetCore.OAuth2Introspection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Streetwriters.Data.Sqlite;
using Notesnook.API.Accessors;
using Notesnook.API.Data;
using Notesnook.API.Authorization;
using Notesnook.API.Extensions;
using Notesnook.API.Hubs;
using Notesnook.API.Interfaces;
using Notesnook.API.Jobs;
using Notesnook.API.Models;
using Notesnook.API.Repositories;
using Notesnook.API.Services;
using Quartz;
using Streetwriters.Common;
using Streetwriters.Common.Extensions;
using Streetwriters.Common.Interfaces;
using Streetwriters.Common.Messages;
using Streetwriters.Common.Models;
using Streetwriters.Common.Services;
using Streetwriters.Data;
using Streetwriters.Data.DbContexts;
using Streetwriters.Data.Interfaces;
using Streetwriters.Data.Repositories;

namespace Notesnook.API
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddDbContext<NotesnookDbContext>(options =>
                options.UseNotesnookSqlite(Constants.DB_CONNECTION_STRING));

            services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();

            JwtSecurityTokenHandler.DefaultMapInboundClaims = false;
            JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

            services.AddDefaultCors();

            services.AddDistributedMemoryCache(delegate (MemoryDistributedCacheOptions cacheOptions)
            {
                cacheOptions.SizeLimit = 16L * 1024 * 1024;
            });

            services.AddAuthorization(options =>
            {
                options.AddPolicy("Notesnook", policy =>
                {
                    policy.AuthenticationSchemes.Add("introspection");
                    policy.RequireAuthenticatedUser();
                    policy.Requirements.Add(new NotesnookUserRequirement());
                });
                options.AddPolicy("Sync", policy =>
                {
                    policy.AuthenticationSchemes.Add("introspection");
                    policy.RequireAuthenticatedUser();
                    policy.Requirements.Add(new SyncRequirement());
                });

                options.AddPolicy(InboxApiKeyAuthenticationDefaults.AuthenticationScheme, policy =>
                {
                    policy.AuthenticationSchemes.Add(InboxApiKeyAuthenticationDefaults.AuthenticationScheme);
                    policy.RequireAuthenticatedUser();
                });

                options.DefaultPolicy = options.GetPolicy("Notesnook") ?? throw new Exception("Notesnook policy not found");
            }).AddSingleton<IAuthorizationMiddlewareResultHandler, AuthorizationResultTransformer>();

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer("introspection", options =>
            {
                // Access tokens are self-contained JWTs now (Config.cs: AccessTokenType.Jwt),
                // validated locally against Identity's signing keys. The old reference-token
                // + /connect/introspect round-trip was taking ~8 s per call under load and
                // stalling every SignalR sync connect until the client timed out.
                options.Authority = Servers.IdentityServer.ToString();
                options.RequireHttpsMetadata = false;
                options.MapInboundClaims = false;
                options.SaveToken = true;
                options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidAudience = "notesnook",
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    NameClaimType = "sub",
                    RoleClaimType = "role",
                    ClockSkew = TimeSpan.FromMinutes(5),
                };
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = (context) =>
                    {
                        // Authorization header for normal requests; ?access_token= for the
                        // SignalR WebSocket (it can't set headers on the upgrade).
                        var fromHeader = TokenRetrieval.FromAuthorizationHeader();
                        var fromQuery = TokenRetrieval.FromQueryString();
                        context.Token = fromHeader(context.Request) ?? fromQuery(context.Request);
                        return Task.CompletedTask;
                    },
                    OnTokenValidated = (context) =>
                    {
                        if (long.TryParse(context.Principal?.FindFirst("exp")?.Value, out long expiryTime))
                        {
                            context.Properties.ExpiresUtc = DateTimeOffset.FromUnixTimeSeconds(expiryTime);
                        }
                        context.Properties.AllowRefresh = true;
                        context.Properties.IsPersistent = true;
                        context.HttpContext.User = context.Principal ?? throw new Exception("No principal found in token.");
                        return Task.CompletedTask;
                    }
                };
            })
            .AddScheme<InboxApiKeyAuthenticationSchemeOptions, InboxApiKeyAuthenticationHandler>(
                InboxApiKeyAuthenticationDefaults.AuthenticationScheme,
                options => { }
            );

            services.AddScoped<DbContext>(sp => sp.GetRequiredService<NotesnookDbContext>());
            services.AddScoped<IDbContext>(sp => new EfDbContext(sp.GetRequiredService<NotesnookDbContext>()));
            services.AddScoped<IUnitOfWork, UnitOfWork>();

            services.AddRepository<UserSettings>()
                    .AddRepository<Monograph>()
                    .AddRepository<Announcement>()
                    .AddRepository<InboxApiKey>()
                    .AddRepository<InboxSyncItem>();

            services.AddScoped<ISyncItemsRepositoryAccessor, SyncItemsRepositoryAccessor>();
            services.AddScoped<SyncDeviceService>();
            services.AddScoped<IUserService, UserService>();
            services.AddScoped<IS3Service, S3Service>();
            services.AddScoped<IURLAnalyzer, URLAnalyzer>();

            services.AddWampServiceAccessor(Servers.NotesnookAPI);

            services.AddControllers();

            services.AddHealthChecks();

            var signalR = services.AddSignalR((hub) =>
            {
                hub.MaximumReceiveMessageSize = 100 * 1024 * 1024;
                hub.KeepAliveInterval = TimeSpan.FromSeconds(15);
                hub.ClientTimeoutInterval = TimeSpan.FromMinutes(10);
                // Default is 15s; give a slow/buffered proxy (Cloudflare) more room
                // for the client's first handshake frame to arrive.
                hub.HandshakeTimeout = TimeSpan.FromSeconds(30);
                hub.EnableDetailedErrors = true;
            }).AddMessagePackProtocol().AddJsonProtocol();

            if (!string.IsNullOrEmpty(Constants.SIGNALR_REDIS_CONNECTION_STRING))
            {
                services.AddHealthChecks()
                        .AddRedis(Constants.SIGNALR_REDIS_CONNECTION_STRING, tags: ["ready"]);
                signalR.AddStackExchangeRedis(options =>
                {
                    options.Configuration = ConfigurationOptions.Parse(Constants.SIGNALR_REDIS_CONNECTION_STRING);
                    options.Configuration.AbortOnConnectFail = false;
                    options.Configuration.ConnectRetry = 5;
                    options.Configuration.ReconnectRetryPolicy = new ExponentialRetry(5000, 30000);
                    options.Configuration.KeepAlive = 60;
                    options.Configuration.ConnectTimeout = 5000;
                    options.Configuration.SyncTimeout = 5000;
                });
            }

            services.AddResponseCompression(options =>
            {
                options.EnableForHttps = true;
                options.Providers.Add<BrotliCompressionProvider>();
                options.Providers.Add<GzipCompressionProvider>();
            });

            services.Configure<BrotliCompressionProviderOptions>(options =>
            {
                options.Level = CompressionLevel.Fastest;
            });
            services.Configure<GzipCompressionProviderOptions>(options =>
            {
                options.Level = CompressionLevel.Fastest;
            });

            // OpenTelemetry / Prometheus metrics removed — no scraper on a self-hosted
            // single-user box. SyncEventCounterSource (EventSource) still works.

            services.AddQuartzHostedService(q =>
            {
                q.WaitForJobsToComplete = false;
                q.AwaitApplicationStarted = true;
                q.StartDelay = TimeSpan.FromMinutes(1);
            }).AddQuartz(q =>
            {
                q.UseMicrosoftDependencyInjectionJobFactory();

                var jobKey = new JobKey("DeviceCleanupJob");
                q.AddJob<DeviceCleanupJob>(opts => opts.WithIdentity(jobKey));
                q.AddTrigger(opts => opts
                    .ForJob(jobKey)
                    .WithIdentity("DeviceCleanup-trigger")
                    // first of every month
                    .WithCronSchedule("0 0 0 1 * ? *"));
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app.UseForwardedHeadersWithKnownProxies(env);

            // Response compression must not wrap the SignalR WebSocket pipeline.
            app.UseWhen(
                ctx => !ctx.Request.Path.StartsWithSegments("/hubs"),
                branch => branch.UseResponseCompression()
            );

            // Vendored monograph unlock-page assets (libsodium sumo). Served from
            // wwwroot/ at /assets/*; long-cache since the file is content-stable.
            app.UseStaticFiles(new StaticFileOptions
            {
                OnPrepareResponse = ctx =>
                    ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable"
            });

            app.UseWebSockets(new Microsoft.AspNetCore.Builder.WebSocketOptions
            {
                KeepAliveInterval = TimeSpan.FromSeconds(30),
                KeepAliveTimeout = TimeSpan.FromSeconds(60),
            });

            app.UseCors("notesnook");
            app.UseVersion(Servers.NotesnookAPI);

            app.UseWamp(WampServers.NotesnookServer, (realm, server) =>
            {
                realm.Subscribe<DeleteUserMessage>(IdentityServerTopics.DeleteUserTopic, async (ev) =>
                {
                    IUserService service = app.GetScopedService<IUserService>();
                    await service.DeleteUserAsync(ev.UserId);
                });

                realm.Subscribe<ClearCacheMessage>(IdentityServerTopics.ClearCacheTopic, (ev) =>
                {
                    IDistributedCache cache = app.GetScopedService<IDistributedCache>();
                    ev.Keys.ForEach((key) => cache.Remove(key));
                });
            });

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapHealthChecks("/health");
                // The Notesnook client validates the "Monograph server" URL by
                // GET {url}/api/version and expects id "monograph" (prod monogr.ph
                // returns {"version":1,"id":"monograph","instance":"..."}). We render
                // monographs in-process, so answer here.
                endpoints.MapGet("/api/version", () => Microsoft.AspNetCore.Http.Results.Json(new
                {
                    version = Constants.COMPATIBILITY_VERSION,
                    id = "monograph",
                    instance = Constants.INSTANCE_NAME
                }));
                endpoints.MapHub<SyncV2Hub>("/hubs/sync/v2", options =>
                {
                    options.CloseOnAuthenticationExpiration = false;
                    options.Transports = HttpTransportType.WebSockets;
                });
            });
        }
    }
}

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
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using IdentityServer4.EntityFramework.DbContexts;
using Streetwriters.Common;
using Streetwriters.Identity.Data;

namespace Streetwriters.Identity
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
#if (DEBUG || STAGING)
            DotNetEnv.Env.TraversePath().Load(".env.local");
#else
            DotNetEnv.Env.TraversePath().Load(".env");
#endif
            IHost host = CreateHostBuilder(args).Build();

            using (var scope = host.Services.CreateScope())
            {
                var sp = scope.ServiceProvider;
                await sp.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
                await sp.GetRequiredService<PersistedGrantDbContext>().Database.MigrateAsync();

                // Dev/E2E only: seed a fully-provisioned password user (mirrors the
                // self-hosted signup in UserAccountService). No-op unless E2E_SEED_EMAIL is set.
                if (Environment.GetEnvironmentVariable("E2E_SEED_EMAIL") is string seedEmail)
                {
                    var um = sp.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Streetwriters.Common.Models.User>>();
                    var rm = sp.GetRequiredService<Microsoft.AspNetCore.Identity.RoleManager<Streetwriters.Common.Models.Role>>();
                    if (await rm.FindByNameAsync("notesnook") is null)
                        await rm.CreateAsync(new Streetwriters.Common.Models.Role { Name = "notesnook" });

                    if (await um.FindByEmailAsync(seedEmail) is null)
                    {
                        var u = new Streetwriters.Common.Models.User
                        {
                            Email = seedEmail,
                            UserName = seedEmail,
                            EmailConfirmed = true,
                            SecurityStamp = Guid.NewGuid().ToString()
                        };
                        var r = await um.CreateAsync(u, Environment.GetEnvironmentVariable("E2E_SEED_PASSWORD") ?? "");
                        if (r.Succeeded)
                        {
                            await um.AddToRoleAsync(u, "notesnook");
                            await um.AddClaimAsync(u, new System.Security.Claims.Claim("notesnook:status", "believer"));
                        }
                        Console.WriteLine($"[E2E] seed user {seedEmail}: {(r.Succeeded ? "created + role notesnook + believer" : string.Join(";", r.Errors))}");
                    }
                }
            }

            await host.RunAsync();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
            .ConfigureLogging((options) =>
            {
                options.AddConsole();
                options.AddSimpleConsole();
            })
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder
                    .UseStartup<Startup>()
                    .UseKestrel((options) =>
                    {
                        options.Limits.MaxRequestBodySize = long.MaxValue;
                        options.ListenAnyIP(Servers.IdentityServer.Port);
                        if (Servers.IdentityServer.IsSecure && Servers.IdentityServer.SSLCertificate != null)
                        {
                            options.ListenAnyIP(443, listenerOptions =>
                            {
                                listenerOptions.UseHttps(Servers.IdentityServer.SSLCertificate);
                            });
                        }
                    });
            });
    }
}

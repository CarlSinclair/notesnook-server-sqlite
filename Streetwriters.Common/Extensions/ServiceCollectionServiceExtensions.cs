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

using Microsoft.Extensions.DependencyInjection;
using Streetwriters.Common.Accessors;
using Streetwriters.Data.Repositories;

namespace Streetwriters.Common.Extensions
{
    public static class ServiceCollectionServiceExtensions
    {
        public static IServiceCollection AddWampServiceAccessor(this IServiceCollection services, Server server)
        {
            services.AddSingleton<WampServiceAccessor>((provider) => new WampServiceAccessor(server));
            services.AddHostedService(provider => provider.GetRequiredService<WampServiceAccessor>());
            return services;
        }

        /// <summary>
        /// Registers a generic <see cref="Repository{T}"/> for entity <typeparamref name="T"/>.
        /// The entity must be mapped by the app's EF Core DbContext (see NotesnookDbContext).
        /// The <paramref name="collectionName"/>/<paramref name="database"/> args are retained
        /// for call-site compatibility and ignored (SQLite backend).
        /// </summary>
        public static IServiceCollection AddRepository<T>(this IServiceCollection services, string? collectionName = null, string? database = null) where T : class
        {
            services.AddScoped<Repository<T>>();
            return services;
        }

        public static IServiceCollection AddDefaultCors(this IServiceCollection services)
        {
            services.AddCors(options =>
            {
                options.AddPolicy("notesnook", (b) =>
                {
                    if (Constants.NOTESNOOK_CORS_ORIGINS.Length <= 0)
                        b.AllowAnyOrigin();
                    else
                        b.WithOrigins(Constants.NOTESNOOK_CORS_ORIGINS);

                    b.AllowAnyMethod()
                    .AllowAnyHeader()
                    .WithExposedHeaders(["X-Object-Size", "Content-Length"]);
                });
            });
            return services;
        }
    }
}

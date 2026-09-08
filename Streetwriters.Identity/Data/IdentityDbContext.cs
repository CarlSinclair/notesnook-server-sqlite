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

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Streetwriters.Common.Models;

namespace Streetwriters.Identity.Data
{
    /// <summary>
    /// ASP.NET Identity store (AspNetUsers/Roles/Claims/...). Replaces
    /// AspNetCore.Identity.Mongo. Distinct migrations-history table so it can
    /// share the single notesnook.db file with the sync + IdentityServer contexts.
    /// </summary>
    public class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
        : IdentityDbContext<User, Role, string>(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Expose AspNetUserClaims as User.Claims (lazy-loaded) to keep the old
            // MongoUser.Claims call sites working unchanged.
            builder.Entity<User>()
                .HasMany(u => u.Claims)
                .WithOne()
                .HasForeignKey(c => c.UserId)
                .IsRequired();
        }
    }
}

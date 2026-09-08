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

using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Streetwriters.Data.Sqlite
{
    public static class SqliteDataOptionsExtensions
    {
        /// <summary>
        /// Configure a DbContext for the Notesnook SQLite setup: creates the parent
        /// directory, opens in read-write-create mode, attaches the pragma interceptor
        /// (WAL + busy_timeout + foreign_keys), and sets a sane command timeout.
        /// </summary>
        /// <param name="connectionStringOrPath">
        /// Either a full connection string ("Data Source=/path/x.db;...") or a bare file path.
        /// </param>
        public static DbContextOptionsBuilder UseNotesnookSqlite(
            this DbContextOptionsBuilder builder, string connectionStringOrPath,
            string? migrationsHistoryTable = null, string? migrationsAssembly = null)
        {
            var csb = connectionStringOrPath.Contains('=')
                ? new SqliteConnectionStringBuilder(connectionStringOrPath)
                : new SqliteConnectionStringBuilder { DataSource = connectionStringOrPath };

            if (csb.Mode == SqliteOpenMode.ReadWriteCreate)
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(csb.DataSource));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            }

            builder.UseSqlite(csb.ToString(), o =>
            {
                o.CommandTimeout(30);
                // Each DbContext sharing the one db file keeps its own history table.
                if (!string.IsNullOrEmpty(migrationsHistoryTable))
                    o.MigrationsHistoryTable(migrationsHistoryTable);
                // For contexts that live in another assembly (IdentityServer4's stores).
                if (!string.IsNullOrEmpty(migrationsAssembly))
                    o.MigrationsAssembly(migrationsAssembly);
            });
            builder.AddInterceptors(new SqlitePragmaInterceptor());
            return builder;
        }
    }
}

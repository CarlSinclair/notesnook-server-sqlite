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

using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Streetwriters.Data.Sqlite
{
    /// <summary>
    /// Applies connection-scoped pragmas every time EF Core opens a SQLite connection.
    /// The per-connection pragmas are always safe. <c>journal_mode=WAL</c> is a write and
    /// must be tolerated failing: EF opens read-only connections for existence probes.
    /// </summary>
    public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
    {
        // Safe on any connection, including read-only.
        private const string SessionPragmas =
            "PRAGMA busy_timeout=5000;" +
            "PRAGMA foreign_keys=ON;" +
            "PRAGMA temp_store=MEMORY;";

        // Persistent, database-level, and a write. Only succeeds on a writable connection;
        // needs to succeed once, ever.
        private const string WalPragma = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";

        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        {
            Exec(connection, SessionPragmas, mustSucceed: true);
            Exec(connection, WalPragma, mustSucceed: false);
        }

        public override async Task ConnectionOpenedAsync(
            DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            await ExecAsync(connection, SessionPragmas, mustSucceed: true, cancellationToken);
            await ExecAsync(connection, WalPragma, mustSucceed: false, cancellationToken);
        }

        private static void Exec(DbConnection c, string sql, bool mustSucceed)
        {
            try { using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
            catch when (!mustSucceed) { }
        }

        private static async Task ExecAsync(DbConnection c, string sql, bool mustSucceed, CancellationToken ct)
        {
            try { await using var cmd = c.CreateCommand(); cmd.CommandText = sql; await cmd.ExecuteNonQueryAsync(ct); }
            catch when (!mustSucceed) { }
        }
    }
}

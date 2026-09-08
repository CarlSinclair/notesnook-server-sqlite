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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Streetwriters.Data.Interfaces;

namespace Streetwriters.Data.DbContexts
{
    /// <summary>
    /// <see cref="IDbContext"/> implementation over a plain EF Core <see cref="DbContext"/>.
    /// Buffers write commands and flushes them inside a single SQLite transaction on
    /// <see cref="SaveChanges"/>. Faithful replacement for the old Mongo
    /// session + WithTransaction MongoDbContext.
    ///
    /// Register (per app) as:
    ///   services.AddScoped&lt;IDbContext&gt;(sp =&gt; new EfDbContext(sp.GetRequiredService&lt;MyDbContext&gt;()));
    /// </summary>
    public sealed class EfDbContext(DbContext db) : IDbContext
    {
        private readonly List<Func<CancellationToken, Task>> _commands = [];

        public void AddCommand(Func<CancellationToken, Task> command) => _commands.Add(command);

        public async Task<int> SaveChanges()
        {
            if (_commands.Count == 0) return 0;
            var count = _commands.Count;

            var strategy = db.Database.CreateExecutionStrategy();
            var committed = await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync();
                try
                {
                    // A DbContext is not concurrency-safe, so commands run sequentially.
                    // ExecuteDelete/ExecuteUpdate calls inside a command run immediately
                    // but enlist in this transaction; tracked changes flush below.
                    foreach (var command in _commands)
                        await command(CancellationToken.None);

                    await db.SaveChangesAsync();
                    await tx.CommitAsync();
                    return true;
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync();
                    // TODO: route through Slogger like the rest of the codebase.
                    await Console.Error.WriteLineAsync(ex.ToString());
                    return false;
                }
            });

            _commands.Clear();
            db.ChangeTracker.Clear();
            return committed ? count : 0;
        }

        public void Dispose() => GC.SuppressFinalize(this);
    }
}

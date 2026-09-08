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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Notesnook.API.Data;
using Notesnook.API.Models;
using Streetwriters.Data.Interfaces;
using Streetwriters.Data.Repositories;

namespace Notesnook.API.Repositories
{
    /// <summary>
    /// Repository for one logical sync collection (notes, notebooks, content, ...).
    /// All logical collections share a single <c>sync_items</c> table, discriminated
    /// by <see cref="SyncItem.Type"/>. Was one <c>IMongoCollection&lt;SyncItem&gt;</c> per collection.
    /// </summary>
    public class SyncItemsRepository : Repository<SyncItem>
    {
        private const int MaxItemBytes = 15 * 1024 * 1024;

        private readonly string type;
        private readonly NotesnookDbContext context;
        private readonly ILogger<SyncItemsRepository> logger;

        private static readonly HashSet<string> ALGORITHMS = [Algorithms.Default];

        public SyncItemsRepository(IDbContext dbContext, NotesnookDbContext db, string type, ILogger<SyncItemsRepository> logger)
            : base(dbContext, db)
        {
            this.type = type;
            this.context = db;
            this.logger = logger;
        }

        /// <summary>
        /// Streams items for a user. When <paramref name="all"/> is false, restricted
        /// to <paramref name="ids"/>. <paramref name="batchSize"/> is advisory.
        /// </summary>
        public async IAsyncEnumerable<SyncItem> FindItemsById(
            string userId, IEnumerable<string> ids, bool all, int batchSize)
        {
            IQueryable<SyncItem> query = context.SyncItems.AsNoTracking()
                .Where(i => i.UserId == userId && i.Type == type);

            if (!all)
            {
                var idList = ids as ICollection<string> ?? ids.ToList();
                if (idList.Count == 0) yield break;
                query = query.Where(i => idList.Contains(i.ItemId!));
            }

            await foreach (var item in query.AsAsyncEnumerable())
                yield return item;
        }

        public void DeleteByUserId(string userId)
        {
            dbContext.AddCommand(ct =>
                context.SyncItems.Where(i => i.UserId == userId && i.Type == type).ExecuteDeleteAsync(ct));
        }

        /// <summary>
        /// Buffered batch upsert into <c>sync_items</c>. Validates each item exactly as
        /// the Mongo version did, then enqueues an ON CONFLICT upsert executed inside
        /// the unit-of-work transaction.
        /// </summary>
        public void UpsertMany(IEnumerable<SyncItem> items, string userId)
        {
            var toWrite = new List<SyncItem>();
            foreach (var item in items)
            {
                if (item.Length > MaxItemBytes)
                    throw new Exception($"Size of item \"{item.ItemId}\" is too large. Maximum allowed size is 15 MB.");
                if (!ALGORITHMS.Contains(item.Algorithm))
                    throw new Exception($"Invalid alg identifier {item.Algorithm}");
                if (!IsBase64String(item.Cipher))
                {
                    logger.LogError("Corrupted item {ItemId} in collection {CollectionName}. Length: {Length}, Cipher: {Cipher}",
                        item.ItemId, type, item.Length, item.Cipher);
                    throw new Exception($"Corrupted item \"{item.ItemId}\" in collection \"{type}\". Please report this error to support@streetwriters.co.");
                }
                if (item.ItemId == null)
                    throw new Exception("Item does not have an ItemId.");

                item.UserId = userId;
                item.Type = type;
                item.DateSynced = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                toWrite.Add(item);
            }

            if (toWrite.Count == 0) return;

            dbContext.AddCommand(async ct =>
            {
                const string sql =
                    "INSERT INTO sync_items " +
                    "(UserId, Type, ItemId, IV, Cipher, Length, Version, Algorithm, KeyVersion, DateSynced) " +
                    "VALUES ({0},{1},{2},{3},{4},{5},{6},{7},{8},{9}) " +
                    "ON CONFLICT(UserId, Type, ItemId) DO UPDATE SET " +
                    "IV=excluded.IV, Cipher=excluded.Cipher, Length=excluded.Length, " +
                    "Version=excluded.Version, Algorithm=excluded.Algorithm, " +
                    "KeyVersion=excluded.KeyVersion, DateSynced=excluded.DateSynced";

                foreach (var i in toWrite)
                {
                    object?[] p =
                    [
                        i.UserId!, i.Type, i.ItemId!, i.IV, i.Cipher, i.Length, i.Version,
                        i.Algorithm, i.KeyVersion, i.DateSynced
                    ];
                    await context.Database.ExecuteSqlRawAsync(sql, p, ct);
                }
            });
        }

        private static bool IsBase64String(string value)
        {
            if (value == null || value.Length == 0 || value.Contains(' ') || value.Contains('\t') || value.Contains('\r') || value.Contains('\n'))
                return false;
            var index = value.Length - 1;
            if (value[index] == '=') index--;
            if (value[index] == '=') index--;
            for (var i = 0; i <= index; i++)
                if (IsInvalidBase64Char(value[i]))
                    return false;
            return true;
        }

        private static bool IsInvalidBase64Char(char value)
        {
            var code = (int)value;
            if (code >= 48 && code <= 57) return false;
            if (code >= 65 && code <= 90) return false;
            if (code >= 97 && code <= 122) return false;
            return code != 45 && code != 95;
        }
    }
}

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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Notesnook.API.Data;
using Notesnook.API.Models;

namespace Notesnook.API.Services
{
    public readonly record struct ItemKey(string ItemId, string Type)
    {
        public override string ToString() => $"{ItemId}:{Type}";
    }

    /// <summary>
    /// Per-device "not yet pulled" id tracking. Was MongoDB SyncDevice +
    /// DeviceIdsChunk (array chunking to beat the 16 MB doc limit); now plain rows
    /// in <c>device_pending_ids</c> with buckets "unsynced" / "pending".
    /// </summary>
    public class SyncDeviceService(NotesnookDbContext db, ILogger<SyncDeviceService> logger)
    {
        private const string Unsynced = "unsynced";
        private const string Pending = "pending";

        public async Task<HashSet<ItemKey>> GetIdsAsync(string userId, string deviceId, string bucket)
        {
            var rows = await db.DevicePendingIds.AsNoTracking()
                .Where(x => x.UserId == userId && x.DeviceId == deviceId && x.Bucket == bucket)
                .Select(x => new { x.ItemId, x.Type })
                .ToListAsync();
            return rows.Select(r => new ItemKey(r.ItemId, r.Type)).ToHashSet();
        }

        public async Task AppendIdsAsync(string userId, string deviceId, string bucket, IEnumerable<ItemKey> ids)
        {
            const string sql =
                "INSERT OR IGNORE INTO device_pending_ids (UserId, DeviceId, Bucket, ItemId, Type) VALUES ({0},{1},{2},{3},{4})";
            foreach (var id in ids)
                await db.Database.ExecuteSqlRawAsync(sql, userId, deviceId, bucket, id.ItemId, id.Type);
        }

        public async Task WriteIdsAsync(string userId, string deviceId, string bucket, IEnumerable<ItemKey> ids)
        {
            await db.DevicePendingIds
                .Where(x => x.UserId == userId && x.DeviceId == deviceId && x.Bucket == bucket)
                .ExecuteDeleteAsync();
            await AppendIdsAsync(userId, deviceId, bucket, ids);
        }

        public async Task<HashSet<ItemKey>> FetchUnsyncedIdsAsync(string userId, string deviceId)
        {
            var device = await GetDeviceAsync(userId, deviceId);
            if (device == null || device.IsSyncReset) return [];

            var unsyncedIds = await GetIdsAsync(userId, deviceId, Unsynced);
            var pendingIds = await GetIdsAsync(userId, deviceId, Pending);
            unsyncedIds = [.. unsyncedIds, .. pendingIds];
            if (unsyncedIds.Count == 0) return [];

            await db.DevicePendingIds
                .Where(x => x.UserId == userId && x.DeviceId == deviceId && x.Bucket == Unsynced)
                .ExecuteDeleteAsync();
            await WriteIdsAsync(userId, deviceId, Pending, unsyncedIds);
            return unsyncedIds;
        }

        public Task WritePendingIdsAsync(string userId, string deviceId, HashSet<ItemKey> ids)
            => WriteIdsAsync(userId, deviceId, Pending, ids);

        public async Task ResetAsync(string userId, string deviceId)
        {
            await db.SyncDevices
                .Where(x => x.UserId == userId && x.DeviceId == deviceId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsSyncReset, false));
            await db.DevicePendingIds
                .Where(x => x.UserId == userId && x.DeviceId == deviceId && x.Bucket == Pending)
                .ExecuteDeleteAsync();
        }

        public Task<SyncDevice?> GetDeviceAsync(string userId, string deviceId)
            => db.SyncDevices.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId && x.DeviceId == deviceId);

        public async IAsyncEnumerable<SyncDevice> ListDevicesAsync(string userId)
        {
            await foreach (var device in db.SyncDevices.AsNoTracking().Where(x => x.UserId == userId).AsAsyncEnumerable())
                yield return device;
        }

        public async Task ResetDevicesAsync(string userId)
        {
            await db.SyncDevices.Where(x => x.UserId == userId).ExecuteDeleteAsync();
            await db.DevicePendingIds.Where(x => x.UserId == userId).ExecuteDeleteAsync();
        }

        public Task UpdateLastAccessTimeAsync(string userId, string deviceId)
        {
            // EF can't translate a method call inside SetProperty's value expression.
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return db.SyncDevices
                .Where(x => x.UserId == userId && x.DeviceId == deviceId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastAccessTime, now));
        }

        public async Task AddIdsToOtherDevicesAsync(string userId, string deviceId, IEnumerable<ItemKey> ids)
        {
            var keys = ids as ICollection<ItemKey> ?? ids.ToList();
            await UpdateLastAccessTimeAsync(userId, deviceId);
            await foreach (var device in ListDevicesAsync(userId))
            {
                if (device.DeviceId == deviceId || device.IsSyncReset) continue;
                await AppendIdsAsync(userId, device.DeviceId, Unsynced, keys);
            }
        }

        public async Task AddIdsToAllDevicesAsync(string userId, IEnumerable<ItemKey> ids)
        {
            var keys = ids as ICollection<ItemKey> ?? ids.ToList();
            await foreach (var device in ListDevicesAsync(userId))
            {
                if (device.IsSyncReset) continue;
                await AppendIdsAsync(userId, device.DeviceId, Unsynced, keys);
            }
        }

        public async Task<SyncDevice> RegisterDeviceAsync(string userId, string deviceId)
        {
            var newDevice = new SyncDevice
            {
                UserId = userId,
                DeviceId = deviceId,
                LastAccessTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                IsSyncReset = true
            };
            db.SyncDevices.Add(newDevice);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return newDevice;
        }

        public async Task UnregisterDeviceAsync(string userId, string deviceId)
        {
            await db.SyncDevices.Where(x => x.UserId == userId && x.DeviceId == deviceId).ExecuteDeleteAsync();
            await db.DevicePendingIds.Where(x => x.UserId == userId && x.DeviceId == deviceId).ExecuteDeleteAsync();
        }
    }
}

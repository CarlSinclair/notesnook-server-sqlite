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

using Microsoft.EntityFrameworkCore;
using Notesnook.API.Models;

namespace Notesnook.API.Data
{
    public class NotesnookDbContext(DbContextOptions<NotesnookDbContext> options) : DbContext(options)
    {
        public DbSet<SyncItem> SyncItems => Set<SyncItem>();
        public DbSet<SyncDevice> SyncDevices => Set<SyncDevice>();
        public DbSet<DevicePendingId> DevicePendingIds => Set<DevicePendingId>();
        public DbSet<UserSettings> UserSettings => Set<UserSettings>();
        public DbSet<InboxApiKey> InboxApiKeys => Set<InboxApiKey>();
        public DbSet<InboxSyncItem> InboxSyncItems => Set<InboxSyncItem>();
        public DbSet<Monograph> Monographs => Set<Monograph>();
        public DbSet<Announcement> Announcements => Set<Announcement>();

        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<SyncItem>(e =>
            {
                e.ToTable("sync_items");
                e.HasKey(x => new { x.UserId, x.Type, x.ItemId });
                e.Property(x => x.UserId).IsRequired();
                e.Property(x => x.Type).IsRequired();
                e.Property(x => x.ItemId).IsRequired();
                e.Property(x => x.IV);
                e.Property(x => x.Cipher);
                e.Property(x => x.Length);
                e.Property(x => x.Version);
                e.Property(x => x.Algorithm);
                e.Property(x => x.KeyVersion);
                e.Property(x => x.DateSynced);
                e.HasIndex(x => x.UserId);
            });

            b.Entity<SyncDevice>(e =>
            {
                e.ToTable("sync_devices");
                e.HasKey(x => new { x.UserId, x.DeviceId });
                e.Property(x => x.LastAccessTime);
                e.Property(x => x.IsSyncReset);
                e.Property(x => x.AppVersion);
                e.Property(x => x.DatabaseVersion);
            });

            b.Entity<DevicePendingId>(e =>
            {
                e.ToTable("device_pending_ids");
                e.HasKey(x => new { x.UserId, x.DeviceId, x.Bucket, x.ItemId, x.Type });
                e.HasIndex(x => new { x.UserId, x.DeviceId, x.Bucket });
            });

            b.Entity<UserSettings>(e =>
            {
                e.ToTable("user_settings");
                e.HasKey(x => x.Id);
                e.HasIndex(x => x.UserId).IsUnique();
                e.Property(x => x.VaultKey).HasJsonColumn();
                e.Property(x => x.AttachmentsKey).HasJsonColumn();
                e.Property(x => x.MonographPasswordsKey).HasJsonColumn();
                e.Property(x => x.DataEncryptionKey).HasJsonColumn();
                e.Property(x => x.LegacyDataEncryptionKey).HasJsonColumn();
                e.Property(x => x.InboxKeys).HasJsonColumn();
                e.Property(x => x.StorageLimit).HasJsonColumn();
            });

            b.Entity<InboxApiKey>(e =>
            {
                e.ToTable("inbox_api_keys");
                e.HasKey(x => x.Id);
                e.HasIndex(x => x.UserId);
                e.HasIndex(x => x.Key).IsUnique();
            });

            b.Entity<InboxSyncItem>(e =>
            {
                e.ToTable("inbox_sync_items");
                e.HasKey(x => x.Id);
                e.HasIndex(x => new { x.UserId, x.ItemId });
            });

            b.Entity<Monograph>(e =>
            {
                e.ToTable("monographs");
                e.HasKey(x => x.Id);
                e.HasIndex(x => x.UserId);
                e.HasIndex(x => x.Slug);
                e.Ignore(x => x.Content);
                e.Property(x => x.EncryptedContent).HasJsonColumn();
                e.Property(x => x.Password).HasJsonColumn();
            });

            b.Entity<Announcement>(e =>
            {
                e.ToTable("announcements");
                e.HasKey(x => x.Id);
                e.Property(x => x.Platforms).HasJsonColumn();
                e.Property(x => x.UserTypes).HasJsonColumn();
                e.Property(x => x.Body).HasJsonColumn();
                e.Property(x => x.UserIds).HasJsonColumn();
                e.Property(x => x.CallToActions).HasJsonColumn();
            });
        }
    }
}

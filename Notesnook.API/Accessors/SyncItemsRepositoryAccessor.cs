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

using Microsoft.Extensions.Logging;
using Notesnook.API.Data;
using Notesnook.API.Interfaces;
using Notesnook.API.Models;
using Notesnook.API.Repositories;
using Streetwriters.Data.Interfaces;
using Streetwriters.Data.Repositories;

namespace Notesnook.API.Accessors
{
    /// <summary>
    /// One <see cref="SyncItemsRepository"/> per logical collection, all over the
    /// single <c>sync_items</c> table (discriminated by type). Plus the plain
    /// <see cref="Repository{T}"/> instances for the non-sync tables.
    /// </summary>
    public class SyncItemsRepositoryAccessor : ISyncItemsRepositoryAccessor
    {
        public SyncItemsRepository Notes { get; }
        public SyncItemsRepository Notebooks { get; }
        public SyncItemsRepository Shortcuts { get; }
        public SyncItemsRepository Relations { get; }
        public SyncItemsRepository Reminders { get; }
        public SyncItemsRepository Contents { get; }
        public SyncItemsRepository LegacySettings { get; }
        public SyncItemsRepository Settings { get; }
        public SyncItemsRepository Attachments { get; }
        public SyncItemsRepository Colors { get; }
        public SyncItemsRepository Vaults { get; }
        public SyncItemsRepository Tags { get; }
        public SyncItemsRepository InboxItemsHistory { get; }

        public Repository<UserSettings> UsersSettings { get; }
        public Repository<Monograph> Monographs { get; }
        public Repository<InboxApiKey> InboxApiKey { get; }
        public Repository<InboxSyncItem> InboxItems { get; }

        public SyncItemsRepositoryAccessor(
            IDbContext dbContext,
            NotesnookDbContext db,
            Repository<UserSettings> usersSettings,
            Repository<Monograph> monographs,
            Repository<InboxApiKey> inboxApiKey,
            Repository<InboxSyncItem> inboxItems,
            ILogger<SyncItemsRepository> logger)
        {
            UsersSettings = usersSettings;
            Monographs = monographs;
            InboxApiKey = inboxApiKey;
            InboxItems = inboxItems;

            SyncItemsRepository Make(string type) => new(dbContext, db, type, logger);

            Notes = Make(Collections.NotesKey);
            Notebooks = Make(Collections.NotebooksKey);
            Contents = Make(Collections.ContentKey);
            Settings = Make(Collections.SettingsKey);
            LegacySettings = Make(Collections.LegacySettingsKey);
            Attachments = Make(Collections.AttachmentsKey);
            Shortcuts = Make(Collections.ShortcutsKey);
            Reminders = Make(Collections.RemindersKey);
            Relations = Make(Collections.RelationsKey);
            Colors = Make(Collections.ColorsKey);
            Vaults = Make(Collections.VaultsKey);
            Tags = Make(Collections.TagsKey);
            InboxItemsHistory = Make(Collections.InboxItemsHistoryKey);
        }
    }
}

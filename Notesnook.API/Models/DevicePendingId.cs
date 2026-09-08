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

namespace Notesnook.API.Models
{
    /// <summary>
    /// One id a given device has not yet pulled. Replaces the MongoDB
    /// <c>DeviceIdsChunk</c> (which only chunked to dodge the 16 MB document limit).
    /// </summary>
    public class DevicePendingId
    {
        public string UserId { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;

        /// <summary>"unsynced" or "pending".</summary>
        public string Bucket { get; set; } = string.Empty;

        public string ItemId { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
    }
}

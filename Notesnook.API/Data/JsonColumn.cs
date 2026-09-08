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
using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Notesnook.API.Data
{
    /// <summary>
    /// Stores an arbitrary reference-typed property as a JSON TEXT column. Portable
    /// across providers (unlike owned-entity JSON mapping) and good enough for the
    /// small nested value objects here (EncryptedData, Limit, arrays, ...).
    /// </summary>
    public static class JsonColumn
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

        public static PropertyBuilder<T?> HasJsonColumn<T>(this PropertyBuilder<T?> builder) where T : class
        {
            var converter = new ValueConverter<T?, string?>(
                v => v == null ? null : JsonSerializer.Serialize(v, Options),
                v => string.IsNullOrEmpty(v) ? null : JsonSerializer.Deserialize<T>(v, Options));

            var comparer = new ValueComparer<T?>(
                (a, b) => JsonSerializer.Serialize(a, Options) == JsonSerializer.Serialize(b, Options),
                v => v == null ? 0 : JsonSerializer.Serialize(v, Options).GetHashCode(),
                v => v == null ? null : JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, Options), Options));

            builder.HasConversion(converter).Metadata.SetValueComparer(comparer);
            builder.HasColumnType("TEXT");
            return builder;
        }
    }
}

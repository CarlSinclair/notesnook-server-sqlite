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
using System.Text;
using Geralt;

namespace Streetwriters.Identity.Helpers
{
    internal class PasswordHelper
    {
        // libsodium's crypto_pwhash_str output buffer (crypto_pwhash_STRBYTES).
        const int HASH_BUFFER = 128;

        public static bool VerifyPassword(string password, string hash)
        {
            // The stored value is an ASCII PHC string ("$argon2id$v=19$m=...").
            // libsodium's verifier wants a 128-byte, NUL-terminated buffer, so
            // re-pad whatever came back from the database (embedded NULs do not
            // round-trip through SQLite TEXT, so the stored string is the bare
            // PHC string with the padding stripped — or, historically, the full
            // 128 bytes).
            var raw = Encoding.UTF8.GetBytes(hash);
            Span<byte> buffer = stackalloc byte[HASH_BUFFER];
            buffer.Clear();
            raw.AsSpan(0, Math.Min(raw.Length, HASH_BUFFER)).CopyTo(buffer);

            try
            {
                return Argon2id.VerifyHash(buffer, Encoding.UTF8.GetBytes(password));
            }
            catch
            {
                return false;
            }
        }

        public static string CreatePasswordHash(string password)
        {
            if (password.Length == 0) throw new ArgumentException("Password must not be empty.", nameof(password));
            Span<byte> hash = stackalloc byte[HASH_BUFFER];
            Argon2id.ComputeHash(hash, Encoding.UTF8.GetBytes(password), 3, 65536);

            // Strip libsodium's NUL padding before it goes into a SQLite TEXT
            // column — otherwise the embedded NUL truncates/mangles the value on
            // the way back out and verification of a *correct* password fails.
            var nul = hash.IndexOf((byte)0);
            return Encoding.UTF8.GetString(hash[..(nul >= 0 ? nul : hash.Length)]);
        }
    }
}

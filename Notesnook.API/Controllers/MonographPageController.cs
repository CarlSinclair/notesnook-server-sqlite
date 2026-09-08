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
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Notesnook.API.Authorization;
using Notesnook.API.Data;
using Notesnook.API.Models;
using Notesnook.API.Services;
using Streetwriters.Common.Extensions;

namespace Notesnook.API.Controllers
{
    /// <summary>
    /// Public, browser-facing monograph pages served from the sync host itself
    /// (no separate monograph-server). One link shape:
    /// {MONOGRAPH_PUBLIC_URL}/share/{key} where {key} is the slug, the monograph
    /// id, or the note's ItemId.
    /// </summary>
    [AllowAnonymous]
    [RequiresFeature("monographs")]
    public class MonographPageController(NotesnookDbContext db, SyncDeviceService syncDeviceService, ILogger<MonographPageController> logger) : ControllerBase
    {
        [HttpGet("/share/{key}")]
        public Task<IActionResult> Share([FromRoute] string key) =>
            RenderAsync(db.Monographs.AsNoTracking().FirstOrDefaultAsync(m => m.Slug == key || m.Id == key || m.ItemId == key), $"viewed_{key}", $"/share/{key}");

        [HttpPost("/share/{key}/viewed")]
        public async Task<IActionResult> ShareViewed([FromRoute] string key)
        {
            var m = await db.Monographs.AsNoTracking().FirstOrDefaultAsync(x => x.Slug == key || x.Id == key || x.ItemId == key);
            if (m != null && !m.Deleted) await RegisterViewAsync(m, $"viewed_{key}", $"/share/{key}");
            return NoContent();
        }

        // ------------------------------------------------------------------------

        private async Task<IActionResult> RenderAsync(Task<Monograph?> lookup, string cookieName, string cookiePath)
        {
            Monograph? m;
            try { m = await lookup; }
            catch (Exception e) { logger.LogError(e, "monograph page lookup failed"); return Html(MonographPage.NotFound(), 404, MonographPage.PlainCsp); }

            if (m == null || m.Deleted)
                return Html(MonographPage.NotFound(), 404, MonographPage.PlainCsp);

            // Password-protected: hand the ciphertext to the browser, which derives
            // the key and decrypts. The view is registered by the unlock page once
            // decryption succeeds (POST .../viewed), so self-destruct only fires on
            // a real read.
            if (m.EncryptedContent != null)
            {
                return Html(MonographPage.Unlock(m.Title, m.EncryptedContent, m.DatePublished), 200, MonographPage.UnlockCsp);
            }

            string noteHtml;
            try
            {
                var decompressed = m.CompressedContent?.DecompressBrotli() ?? "";
                noteHtml = ExtractHtml(decompressed);
            }
            catch (Exception e)
            {
                logger.LogError(e, "monograph content decode failed for {Id}", m.Id);
                noteHtml = "<p><em>(content could not be displayed)</em></p>";
            }

            var page = MonographPage.Plain(m.Title, noteHtml, m.DatePublished);
            await RegisterViewAsync(m, cookieName, cookiePath);
            return Html(page, 200, MonographPage.PlainCsp);
        }

        private static string ExtractHtml(string stored)
        {
            if (string.IsNullOrWhiteSpace(stored)) return "";
            var trimmed = stored.TrimStart();
            if (trimmed.StartsWith("{"))
            {
                try
                {
                    var env = JsonSerializer.Deserialize<MonographContent>(stored);
                    if (env?.Data != null) return env.Data;
                }
                catch { /* fall through: treat as raw html */ }
            }
            return stored;
        }

        private ContentResult Html(string html, int status, string csp)
        {
            Response.StatusCode = status;
            Response.Headers["Content-Security-Policy"] = csp;
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            Response.Headers["Referrer-Policy"] = "no-referrer";
            Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
            return new ContentResult { Content = html, ContentType = "text/html; charset=utf-8", StatusCode = status };
        }

        private async Task RegisterViewAsync(Monograph m, string cookieName, string cookiePath)
        {
            var seen = Request.Cookies.ContainsKey(cookieName);
            if (m.SelfDestruct)
            {
                var row = await db.Monographs.FirstOrDefaultAsync(x => x.Id == m.Id);
                if (row != null)
                {
                    row.Deleted = true;
                    row.ViewCount = 0;
                    row.CompressedContent = null;
                    row.EncryptedContent = null;
                    row.Content = null;
                    row.Password = null;
                    await db.SaveChangesAsync();
                    db.ChangeTracker.Clear();
                }
                await syncDeviceService.AddIdsToAllDevicesAsync(m.UserId!, [new(m.ItemId ?? m.Id, "monograph")]);
            }
            else if (!seen)
            {
                await db.Monographs.Where(x => x.Id == m.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.ViewCount, x => x.ViewCount + 1));

                Response.Cookies.Append(cookieName, "1", new CookieOptions
                {
                    Path = cookiePath,
                    HttpOnly = true,
                    Secure = Request.IsHttps,
                    Expires = DateTimeOffset.UtcNow.AddMonths(1)
                });
            }
        }
    }
}

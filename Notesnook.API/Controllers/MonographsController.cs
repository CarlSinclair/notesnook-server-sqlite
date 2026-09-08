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
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using AngleSharp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NanoidDotNet;
using Notesnook.API.Authorization;
using Notesnook.API.Data;
using Notesnook.API.Extensions;
using Notesnook.API.Models;
using Notesnook.API.Services;
using Streetwriters.Common;
using Streetwriters.Common.Accessors;
using Streetwriters.Common.Enums;
using Streetwriters.Common.Helpers;
using Streetwriters.Common.Interfaces;

namespace Notesnook.API.Controllers
{
    [ApiController]
    [Route("monographs")]
    [Authorize("Sync")]
    [RequiresFeature("monographs")]
    public class MonographsController(NotesnookDbContext db, IURLAnalyzer analyzer, SyncDeviceService syncDeviceService, WampServiceAccessor serviceAccessor, ILogger<MonographsController> logger) : ControllerBase
    {
        const string SVG_PIXEL = "<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><circle r='9'/></svg>";
        private const int MAX_DOC_SIZE = 15 * 1024 * 1024;

        private Task<Monograph?> FindMonographAsync(string userId, Monograph monograph, bool track = false)
        {
            var key = monograph.ItemId ?? monograph.Id;
            var q = db.Monographs.Where(m => m.UserId == userId && (m.Id == key || m.ItemId == key));
            return (track ? q : q.AsNoTracking()).FirstOrDefaultAsync();
        }

        private Task<Monograph?> FindMonographAsync(string itemId) =>
            db.Monographs.AsNoTracking().FirstOrDefaultAsync(m => m.Id == itemId || m.ItemId == itemId);

        private Task<Monograph?> FindMonographBySlugAsync(string slug) =>
            db.Monographs.AsNoTracking().FirstOrDefaultAsync(m => m.Slug == slug);

        private async Task<string> GenerateUniqueSlugAsync(int length = 10, int maxAttempts = 5)
        {
            for (var i = 0; i < maxAttempts; i++)
            {
                var slug = Nanoid.Generate(size: length);
                if (!await db.Monographs.AnyAsync(m => m.Slug == slug)) return slug;
            }
            throw new Exception("Failed to generate unique slug");
        }

        [HttpPost]
        public async Task<IActionResult> PublishAsync([FromQuery] string? deviceId, [FromBody] Monograph monograph)
        {
            try
            {
                var userId = this.User.GetUserId();
                var jti = this.User.FindFirstValue("jti");
                var existing = await FindMonographAsync(userId, monograph, track: true);
                if (existing != null && !existing.Deleted) return await UpdateAsync(deviceId, monograph);

                monograph = await CreateMonographAsync(monograph, userId);
                await UpsertMonographAsync(existing, monograph);
                await MarkMonographForSyncAsync(userId, monograph.ItemId ?? monograph.Id, deviceId, jti);
                return Ok(new { id = monograph.ItemId, datePublished = monograph.DatePublished });
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to publish monograph");
                return BadRequest(new { error = e.Message });
            }
        }

        [HttpPost("v2")]
        public async Task<IActionResult> PublishV2Async([FromQuery] string? deviceId, [FromBody] Monograph monograph)
        {
            try
            {
                var userId = this.User.GetUserId();
                var jti = this.User.FindFirstValue("jti");
                var existing = await FindMonographAsync(userId, monograph, track: true);
                if (existing != null && !existing.Deleted) return await UpdateAsync(deviceId, monograph);

                monograph = await CreateMonographAsync(monograph, userId);
                monograph.Slug = await GenerateUniqueSlugAsync();
                await UpsertMonographAsync(existing, monograph);
                await MarkMonographForSyncAsync(userId, monograph.ItemId ?? monograph.Id, deviceId, jti);
                return Ok(new
                {
                    id = monograph.ItemId,
                    datePublished = monograph.DatePublished,
                    publishUrl = Helpers.UrlHelper.ConstructPublishUrl(monograph)
                });
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to publish monograph");
                return BadRequest(new { error = e.Message });
            }
        }

        [HttpPatch]
        public async Task<IActionResult> UpdateAsync([FromQuery] string? deviceId, [FromBody] Monograph monograph)
        {
            try
            {
                var userId = this.User.GetUserId();
                var jti = this.User.FindFirstValue("jti");
                var existing = await FindMonographAsync(userId, monograph, track: true);
                if (existing == null || existing.Deleted) return NotFound();

                if (monograph.EncryptedContent?.Cipher.Length > MAX_DOC_SIZE || monograph.CompressedContent?.Length > MAX_DOC_SIZE)
                    return base.BadRequest("Monograph is too big. Max allowed size is 15mb.");

                var sanitizationLevel = ContentSanitizationLevel.Unknown;
                if (monograph.EncryptedContent == null)
                {
                    sanitizationLevel = User.IsUserSubscribed() ? ContentSanitizationLevel.Partial : ContentSanitizationLevel.Full;
                    monograph.CompressedContent = (await SanitizeContentAsync(monograph.Content, sanitizationLevel)).CompressBrotli();
                }
                else monograph.Content = null;

                existing.DatePublished = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                existing.CompressedContent = monograph.CompressedContent;
                existing.EncryptedContent = monograph.EncryptedContent;
                existing.SelfDestruct = monograph.SelfDestruct;
                existing.Title = monograph.Title;
                existing.Password = monograph.Password;
                existing.ContentSanitizationLevel = sanitizationLevel;
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();

                await MarkMonographForSyncAsync(userId, monograph.ItemId ?? monograph.Id, deviceId, jti);
                return Ok(new
                {
                    id = monograph.ItemId,
                    datePublished = existing.DatePublished,
                    publishUrl = Helpers.UrlHelper.ConstructPublishUrl(existing)
                });
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to update monograph");
                return BadRequest(new { error = e.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetUserMonographsAsync()
        {
            var userId = this.User.GetUserId();
            var ids = await db.Monographs.AsNoTracking()
                .Where(m => m.UserId == userId && !m.Deleted)
                .Select(m => m.ItemId ?? m.Id)
                .ToListAsync();
            return Ok(ids);
        }

        [HttpGet("{id}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetMonographAsync([FromRoute] string id)
        {
            var monograph = await FindMonographAsync(id);
            if (monograph == null || monograph.Deleted)
                return NotFound(new { error = "invalid_id", error_description = "No such monograph found." });
            return Ok(await ProcessMonographAsync(monograph));
        }

        [HttpGet("{id}/view")]
        [AllowAnonymous]
        public async Task<IActionResult> TrackView([FromRoute] string id)
        {
            var monograph = await FindMonographAsync(id);
            if (monograph == null || monograph.Deleted) return Content(SVG_PIXEL, "image/svg+xml");
            await TrackViewAsync(monograph, $"viewed_{id}", $"/monographs/{id}");
            return Content(SVG_PIXEL, "image/svg+xml");
        }

        [HttpGet("v2/{slug}/view")]
        [AllowAnonymous]
        public async Task<IActionResult> TrackViewV2([FromRoute] string slug)
        {
            var monograph = await FindMonographBySlugAsync(slug);
            if (monograph == null || monograph.Deleted) return Content(SVG_PIXEL, "image/svg+xml");
            await TrackViewAsync(monograph, $"viewed_{slug}", $"/monographs/v2/{slug}");
            return Content(SVG_PIXEL, "image/svg+xml");
        }

        [HttpGet("v2/{slug}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetMonographBySlugAsync([FromRoute] string slug)
        {
            var monograph = await FindMonographBySlugAsync(slug);
            if (monograph == null || monograph.Deleted)
                return NotFound(new { error = "invalid_id", error_description = "No such monograph found." });
            return Ok(await ProcessMonographAsync(monograph));
        }

        [HttpGet("{id}/analytics")]
        [Obsolete("Use GET /monographs/{id}/metadata instead.")]
        public async Task<IActionResult> GetMonographAnalyticsAsync([FromRoute] string id)
        {
            if (!FeatureAuthorizationHelper.IsFeatureAllowed(Features.MONOGRAPH_ANALYTICS, Clients.Notesnook.Id, User))
                return BadRequest(new { error = "Monograph analytics are only available on the Pro & Believer plans." });
            var userId = this.User.GetUserId();
            var monograph = await FindMonographAsync(id);
            if (monograph == null || monograph.Deleted || monograph.UserId != userId) return NotFound();
            return Ok(new { totalViews = monograph.ViewCount });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteAsync([FromQuery] string? deviceId, [FromRoute] string id)
        {
            var userId = this.User.GetUserId();
            var existing = await db.Monographs.FirstOrDefaultAsync(m => (m.Id == id || m.ItemId == id) && m.UserId == userId);
            if (existing == null || existing.Deleted) return Ok();

            var jti = this.User.FindFirstValue("jti");
            existing.Deleted = true;
            existing.ViewCount = 0;
            existing.CompressedContent = null;
            existing.EncryptedContent = null;
            existing.Content = null;
            existing.Password = null;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            await MarkMonographForSyncAsync(userId, id, deviceId, jti);
            return Ok();
        }

        [HttpGet("{id}/metadata")]
        public async Task<IActionResult> GetMetadataAsync([FromRoute] string id)
        {
            var userId = this.User.GetUserId();
            var monograph = await FindMonographAsync(id);
            if (monograph == null || monograph.Deleted || monograph.UserId != userId) return NotFound();
            var isPro = FeatureAuthorizationHelper.IsFeatureAllowed(Features.MONOGRAPH_ANALYTICS, Clients.Notesnook.Id, User);
            return Ok(new
            {
                publishUrl = Helpers.UrlHelper.ConstructPublishUrl(monograph),
                analytics = new { totalViews = isPro ? monograph.ViewCount : 0 }
            });
        }

        // ---- helpers --------------------------------------------------------------

        private async Task UpsertMonographAsync(Monograph? existing, Monograph monograph)
        {
            if (existing == null)
            {
                db.Monographs.Add(monograph);
            }
            else
            {
                monograph.Id = existing.Id;
                db.Entry(existing).CurrentValues.SetValues(monograph);
                existing.EncryptedContent = monograph.EncryptedContent;
                existing.Password = monograph.Password;
            }
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }

        private async Task MarkMonographForSyncAsync(string userId, string monographId, string? deviceId, string? jti)
        {
            if (deviceId == null) return;
            await syncDeviceService.AddIdsToOtherDevicesAsync(userId, deviceId, [new(monographId, "monograph")]);
        }

        private Task MarkMonographForSyncAsync(string userId, string monographId) =>
            syncDeviceService.AddIdsToAllDevicesAsync(userId, [new(monographId, "monograph")]);

        private static readonly (string Selector, string Attribute)[] urlElements =
        [
            ("a", "href"), ("img", "src"), ("iframe", "src"), ("embed", "src"),
            ("object", "data"), ("source", "src"), ("video", "src"), ("audio", "src"),
        ];

        private async Task<Monograph> CreateMonographAsync(Monograph monograph, string userId)
        {
            if (monograph.EncryptedContent == null)
            {
                var sanitizationLevel = User.IsUserSubscribed() ? ContentSanitizationLevel.Partial : ContentSanitizationLevel.Full;
                monograph.CompressedContent = (await SanitizeContentAsync(monograph.Content, sanitizationLevel)).CompressBrotli();
                monograph.ContentSanitizationLevel = sanitizationLevel;
            }
            monograph.UserId = userId;
            monograph.DatePublished = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (monograph.EncryptedContent?.Cipher.Length > MAX_DOC_SIZE || monograph.CompressedContent?.Length > MAX_DOC_SIZE)
                throw new Exception("Monograph is too big. Max allowed size is 15mb.");
            monograph.Deleted = false;
            monograph.ViewCount = 0;
            return monograph;
        }

        private async Task TrackViewAsync(Monograph monograph, string cookieName, string cookiePath)
        {
            var hasVisitedBefore = Request.Cookies.ContainsKey(cookieName);
            if (monograph.SelfDestruct)
            {
                var tracked = await db.Monographs.FirstOrDefaultAsync(m => m.Id == monograph.Id);
                if (tracked != null)
                {
                    tracked.Deleted = true;
                    tracked.ViewCount = 0;
                    tracked.CompressedContent = null;
                    tracked.EncryptedContent = null;
                    tracked.Content = null;
                    await db.SaveChangesAsync();
                    db.ChangeTracker.Clear();
                }
                await MarkMonographForSyncAsync(monograph.UserId!, monograph.ItemId ?? monograph.Id);
            }
            else if (!hasVisitedBefore)
            {
                await db.Monographs.Where(m => m.Id == monograph.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.ViewCount, m => m.ViewCount + 1));

                Response.Cookies.Append(cookieName, "1", new CookieOptions
                {
                    Path = cookiePath,
                    HttpOnly = true,
                    Secure = Request.IsHttps,
                    Expires = DateTimeOffset.UtcNow.AddMonths(1)
                });
            }
        }

        private async Task<Monograph> ProcessMonographAsync(Monograph monograph)
        {
            if (monograph.EncryptedContent == null)
            {
                var isContentUnsanitized = monograph.ContentSanitizationLevel is ContentSanitizationLevel.Partial or ContentSanitizationLevel.Unknown;
                if (!Constants.IS_SELF_HOSTED && isContentUnsanitized && serviceAccessor.UserSubscriptionService != null
                    && !await serviceAccessor.UserSubscriptionService.IsUserSubscribedAsync(Clients.Notesnook.Id, monograph.UserId!))
                {
                    var cleaned = await SanitizeContentAsync(monograph.CompressedContent?.DecompressBrotli(), ContentSanitizationLevel.Full);
                    var compressed = cleaned.CompressBrotli();
                    await db.Monographs.Where(m => m.Id == monograph.Id)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(m => m.CompressedContent, compressed)
                            .SetProperty(m => m.ContentSanitizationLevel, ContentSanitizationLevel.Full));
                    monograph.CompressedContent = compressed;
                }
                monograph.Content = monograph.CompressedContent?.DecompressBrotli();
            }
            monograph.ItemId ??= monograph.Id;
            return monograph;
        }

        private async Task<string> SanitizeContentAsync(string? content, ContentSanitizationLevel level)
        {
            if (string.IsNullOrEmpty(content)) return string.Empty;
            if (Constants.IS_SELF_HOSTED) return content;
            try
            {
                var json = JsonSerializer.Deserialize<MonographContent>(content) ?? throw new Exception("Invalid monograph content.");
                var html = json.Data;
                var config = Configuration.Default.WithDefaultLoader();
                var context = BrowsingContext.New(config);
                var document = await context.OpenAsync(r => r.Content(html));
                if (level == ContentSanitizationLevel.Partial)
                {
                    foreach (var (selector, attribute) in urlElements)
                        foreach (var element in document.QuerySelectorAll(selector))
                        {
                            var url = element.GetAttribute(attribute);
                            if (string.IsNullOrEmpty(url)) continue;
                            if (!await analyzer.IsURLSafeAsync(url))
                            {
                                logger.LogInformation("Malicious URL in <{Selector} {Attribute}>: {Url}", selector, attribute, url);
                                element.RemoveAttribute(attribute);
                            }
                        }
                }
                else if (level == ContentSanitizationLevel.Full)
                {
                    foreach (var element in document.QuerySelectorAll("a,iframe,img,object,svg,button,link"))
                        foreach (var attr in element.Attributes.ToList())
                            element.RemoveAttribute(attr.Name);
                }
                return JsonSerializer.Serialize(new MonographContent { Type = json.Type, Data = document.ToHtml() });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to cleanup monograph content");
                return content;
            }
        }
    }
}

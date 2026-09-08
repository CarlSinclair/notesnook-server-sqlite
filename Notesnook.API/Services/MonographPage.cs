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
using System.Text.Encodings.Web;
using System.Text.Json;
using AngleSharp;
using Notesnook.API.Models;

namespace Notesnook.API.Services
{
    /// <summary>
    /// Server-side renderer for public monograph pages. Plain monographs are
    /// sanitized here and inlined under a no-script CSP; password-protected ones
    /// get an unlock page that derives the key (Argon2i) and decrypts
    /// (XChaCha20-Poly1305) in the viewer's browser via a small pure-JS crypto
    /// module (no WASM, no eval — CSP stays script-src 'self'). The server never
    /// sees the password or the plaintext.
    /// </summary>
    public static class MonographPage
    {
        // No script-src => default-src 'none' governs: no scripts, no inline
        // handlers, no javascript: URLs.
        public const string PlainCsp =
            "default-src 'none'; img-src 'self' https: data:; style-src 'unsafe-inline'; " +
            "font-src https: data:; media-src https: data:; base-uri 'none'; " +
            "form-action 'none'; frame-ancestors 'none'";

        // Unlock page: one same-origin ES module + its imports, a same-origin
        // POST for view tracking. No 'unsafe-inline', no eval of any kind.
        public const string UnlockCsp =
            "default-src 'none'; script-src 'self'; connect-src 'self'; " +
            "img-src 'self' https: data:; style-src 'unsafe-inline'; " +
            "font-src https: data:; media-src https: data:; base-uri 'none'; " +
            "form-action 'none'; frame-ancestors 'none'";

        public static string NotFound() => Document(
            "Not found",
            "<div class=\"card\"><h1>Nothing here</h1><p>This monograph doesn't exist, was unpublished, or has self-destructed.</p></div>",
            head: "", csp: PlainCsp);

        public static string Plain(string? title, string noteHtml, long datePublished)
        {
            var safe = SanitizeNoteHtml(noteHtml);
            var body =
                Header(title, datePublished) +
                $"<article class=\"note\">{safe}</article>" +
                Footer();
            return Document(title ?? "Monograph", body, head: NoteCss, csp: PlainCsp);
        }

        public static string Unlock(string? title, EncryptedData enc, long datePublished)
        {
            // iv/cipher/salt are opaque base64 (urlsafe, no padding) — safe as a
            // non-executable JSON data island.
            var payload = JsonSerializer.Serialize(new { iv = enc.IV, cipher = enc.Cipher, salt = enc.Salt });

            var body =
                Header(title, datePublished) +
                @"<form id=""unlock"" class=""card"">
  <h1>Password protected</h1>
  <p>Enter the password to read this monograph.</p>
  <input id=""pw"" type=""password"" autocomplete=""current-password"" autofocus placeholder=""Password"" />
  <button type=""submit"" id=""go"">Unlock</button>
  <p id=""err"" class=""err"" hidden></p>
</form>
<article class=""note"" id=""out"" hidden></article>" +
                Footer() +
                $"<script type=\"application/json\" id=\"mono-data\">{payload}</script>" +
                "<script type=\"module\" src=\"/assets/monograph/unlock.js\"></script>";

            return Document(title ?? "Monograph", body, head: NoteCss, csp: UnlockCsp);
        }

        // ---- content sanitization (AngleSharp) -----------------------------------

        private static readonly string[] DropTags =
        {
            "script","style","link","meta","base","noscript","template",
            "form","input","button","textarea","select","option","label",
            "object","embed","applet","iframe","frame","frameset","portal"
        };

        private static readonly string[] UrlAttrs = { "href", "src", "xlink:href", "action", "formaction", "poster", "background" };

        public static string SanitizeNoteHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return "";
            try
            {
                var ctx = BrowsingContext.New(Configuration.Default);
                using var doc = ctx.OpenAsync(r => r.Content(html)).GetAwaiter().GetResult();
                var body = doc.Body;
                if (body == null) return "";

                foreach (var el in body.QuerySelectorAll(string.Join(",", DropTags)).ToList())
                    el.Remove();

                foreach (var el in body.QuerySelectorAll("*").ToList())
                {
                    foreach (var attr in el.Attributes.ToList())
                    {
                        var name = attr.Name.ToLowerInvariant();
                        if (name.StartsWith("on")) { el.RemoveAttribute(attr.Name); continue; }
                        if (name == "style" && attr.Value.Contains("expression(", StringComparison.OrdinalIgnoreCase))
                            el.RemoveAttribute(attr.Name);
                        if (Array.IndexOf(UrlAttrs, name) >= 0 && !IsSafeUrl(attr.Value))
                            el.RemoveAttribute(attr.Name);
                    }
                    if (string.Equals(el.TagName, "A", StringComparison.OrdinalIgnoreCase) && el.HasAttribute("href"))
                    {
                        el.SetAttribute("target", "_blank");
                        el.SetAttribute("rel", "noopener noreferrer nofollow");
                    }
                }
                return body.InnerHtml;
            }
            catch
            {
                return "<p><em>(content could not be displayed)</em></p>";
            }
        }

        private static bool IsSafeUrl(string? value)
        {
            var v = (value ?? "").Trim();
            if (v.Length == 0) return true;
            if (v.StartsWith("#") || v.StartsWith("/") || v.StartsWith("./") || v.StartsWith("../")) return true;
            var lower = v.ToLowerInvariant();
            if (lower.StartsWith("data:image/")) return true;
            if (lower.StartsWith("http://") || lower.StartsWith("https://") || lower.StartsWith("mailto:") || lower.StartsWith("tel:")) return true;
            return !lower.Contains(':');
        }

        // ---- shell --------------------------------------------------------------

        private static string Header(string? title, long datePublished)
        {
            var t = string.IsNullOrWhiteSpace(title) ? "Untitled" : Enc(title);
            var when = datePublished > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(datePublished).UtcDateTime.ToString("MMMM d, yyyy")
                : "";
            var sub = when.Length > 0 ? $"<p class=\"meta\">Published {when}</p>" : "";
            return $"<header><h1 class=\"doc-title\">{t}</h1>{sub}</header>";
        }

        private static string Footer() =>
            "<footer><a href=\"https://notesnook.com/\" target=\"_blank\" rel=\"noopener noreferrer\">Published with Notesnook</a></footer>";

        private static string Document(string title, string body, string head, string csp)
        {
            return "<!doctype html><html lang=\"en\"><head>" +
                   "<meta charset=\"utf-8\">" +
                   "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">" +
                   $"<meta http-equiv=\"Content-Security-Policy\" content=\"{Enc(csp)}\">" +
                   "<meta name=\"robots\" content=\"noindex, nofollow\">" +
                   "<meta name=\"referrer\" content=\"no-referrer\">" +
                   $"<title>{Enc(title)}</title>" +
                   $"<style>{BaseCss}{head}</style>" +
                   "</head><body>" + body + "</body></html>";
        }

        private static string Enc(string s) => HtmlEncoder.Default.Encode(s);

        private const string BaseCss = @"
*{box-sizing:border-box}
html,body{margin:0;padding:0}
body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;
 line-height:1.6;color:#1a1a1a;background:#fbfbfa;-webkit-text-size-adjust:100%}
header,article,footer,.card{max-width:720px;margin-inline:auto;padding-inline:20px}
header{padding-top:56px}
.doc-title{font-size:2rem;line-height:1.25;margin:0 0 .25rem}
.meta{color:#6b7280;font-size:.9rem;margin:0}
article.note{padding-top:28px;padding-bottom:40px}
footer{padding:28px 20px 56px;border-top:1px solid #ececec;margin-top:40px}
footer a{color:#8b5cf6;text-decoration:none;font-size:.85rem}
.card{margin-top:64px;background:#fff;border:1px solid #e5e7eb;border-radius:12px;padding:28px;max-width:420px}
.card h1{font-size:1.15rem;margin:0 0 .35rem}
.card p{margin:.35rem 0 0;color:#4b5563;font-size:.92rem}
.card input{width:100%;margin-top:16px;padding:10px 12px;border:1px solid #d1d5db;border-radius:8px;font-size:1rem}
.card button{width:100%;margin-top:12px;padding:10px 12px;border:0;border-radius:8px;background:#8b5cf6;color:#fff;font-size:1rem;cursor:pointer}
.card button[disabled]{opacity:.6;cursor:progress}
.err{color:#dc2626}
@media (prefers-color-scheme:dark){
 body{color:#e5e7eb;background:#0f0f10}
 .meta{color:#9ca3af}
 footer{border-top-color:#26262a}
 .card{background:#18181b;border-color:#2a2a30}
 .card p{color:#9ca3af}
 .card input{background:#0f0f10;border-color:#3f3f46;color:#e5e7eb}
}";

        private const string NoteCss = @"
article.note img{max-width:100%;height:auto}
article.note pre{overflow-x:auto;background:#f3f4f6;padding:14px;border-radius:8px}
article.note code{font-family:ui-monospace,SFMono-Regular,Menlo,monospace;font-size:.9em}
article.note blockquote{margin:1em 0;padding-left:1em;border-left:3px solid #d1d5db;color:#4b5563}
article.note table{border-collapse:collapse;display:block;overflow-x:auto;max-width:100%}
article.note td,article.note th{border:1px solid #d1d5db;padding:6px 10px}
article.note a{color:#7c3aed}
article.note h1,article.note h2,article.note h3{line-height:1.3}
article.note hr{border:0;border-top:1px solid #e5e7eb;margin:2em 0}
@media (prefers-color-scheme:dark){
 article.note pre{background:#18181b}
 article.note blockquote{border-left-color:#3f3f46;color:#9ca3af}
 article.note td,article.note th{border-color:#3f3f46}
 article.note a{color:#a78bfa}
 article.note hr{border-top-color:#26262a}
}";
    }
}

// Monograph password-unlock: derive key (Argon2i) + decrypt (XChaCha20-Poly1305)
// entirely in the viewer's browser. Pure JS (@noble) — no WASM, no eval; the page
// CSP is just `script-src 'self'`. Params match @notesnook/crypto exactly:
//   deriveKey  = crypto_pwhash(32, pw, salt, opslimit=3, memlimit=8 MiB, ARGON2I13)
//   decrypt    = crypto_aead_xchacha20poly1305_ietf_decrypt(cipher, iv, key)
// Base64 throughout is urlsafe, no padding (libsodium variant 7).
import { argon2iAsync } from "/assets/noble/hashes/argon2.js";
import { xchacha20poly1305 } from "/assets/noble/ciphers/chacha.js";

const D = JSON.parse(document.getElementById("mono-data").textContent);
const form = document.getElementById("unlock");
const pw = document.getElementById("pw");
const go = document.getElementById("go");
const err = document.getElementById("err");
const out = document.getElementById("out");

function b64(s) {
  s = String(s).replace(/-/g, "+").replace(/_/g, "/");
  while (s.length % 4) s += "=";
  const bin = atob(s);
  const u = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) u[i] = bin.charCodeAt(i);
  return u;
}

const DROP = /^(script|style|link|meta|base|noscript|template|form|input|button|textarea|select|object|embed|applet|iframe|frame|frameset)$/i;
function scrub(node) {
  for (const el of Array.from(node.children || [])) {
    if (DROP.test(el.tagName)) { el.remove(); continue; }
    for (const a of Array.from(el.attributes)) {
      const n = a.name.toLowerCase();
      const v = (a.value || "").trim().toLowerCase();
      if (n.startsWith("on")) el.removeAttribute(a.name);
      else if (["href", "src", "xlink:href", "poster"].includes(n) &&
               v.includes(":") && !/^(https?:|mailto:|tel:|data:image\/)/.test(v))
        el.removeAttribute(a.name);
    }
    if (el.tagName === "A" && el.hasAttribute("href")) {
      el.setAttribute("target", "_blank");
      el.setAttribute("rel", "noopener noreferrer nofollow");
    }
    scrub(el);
  }
}

form.addEventListener("submit", async (e) => {
  e.preventDefault();
  err.hidden = true;
  go.disabled = true;
  go.textContent = "Unlocking…";
  try {
    const key = await argon2iAsync(pw.value, b64(D.salt), { t: 3, m: 8192, p: 1, dkLen: 32 });
    let plain;
    try {
      plain = xchacha20poly1305(key, b64(D.iv)).decrypt(b64(D.cipher));
    } catch (_) {
      throw new Error("bad-password");
    }
    const text = new TextDecoder().decode(plain);
    let html;
    try { html = JSON.parse(text).data; } catch (_) { html = text; }
    const doc = new DOMParser().parseFromString(html || "", "text/html");
    scrub(doc.body);
    out.innerHTML = doc.body.innerHTML;
    out.hidden = false;
    form.hidden = true;
    try {
      fetch(location.pathname.replace(/\/+$/, "") + "/viewed", { method: "POST", cache: "no-store" });
    } catch (_) {}
  } catch (ex) {
    go.disabled = false;
    go.textContent = "Unlock";
    err.textContent = "Incorrect password.";
    err.hidden = false;
    pw.select();
  }
});

# Game Cache Cleaner — CrestPoint Digital (Fresh Build)
- WPF UI, tray icon (loads Assets/crestpoint.ico if present), weekly scheduler (schtasks), per‑launcher breakdown, excludes, dry‑run
- Headless `--auto-clean` for scheduled runs

## Build single-file EXE
pwsh -ExecutionPolicy Bypass -File .\publish_selfcontained.ps1

## Licensing (Stripe → Cloudflare Worker → App)

- Stripe Product: `Game Cache Cleaner Pro` (£5 one‑time)
- Payment Link: create in Stripe (TEST first), then set in app via `LicenseService.PaymentLinkUrl`.
- Worker Base URL: `https://license-worker.anthonygenther.workers.dev`
- Success URL: `https://license-worker.anthonygenther.workers.dev/claim?session_id={CHECKOUT_SESSION_ID}`
- Webhook: `POST https://license-worker.anthonygenther.workers.dev/webhook/stripe`
- KV Namespace Binding: `LICENSES` (required)

### Worker code

- Location: `worker/`
- Endpoints:
  - `GET /health` — returns `{ ok: true }` if secrets + KV present
  - `POST /webhook/stripe` — verifies signature, mints ES256 license, stores in KV
  - `GET /claim?session_id=...` — returns license token
- Token format: `base64url(jsonPayload).base64url(ecdsaSig)` where payload is:
  `{ licenseId, product:"gcc-pro", seats:1, emailHash: sha256(lowercase email), issuedAt: unix }`

### Deploy

1. Install Wrangler: `npm i -g wrangler`
2. Set secrets:
   - `wrangler secret put STRIPE_SECRET_KEY`
   - `wrangler secret put WEBHOOK_SIGNING_SECRET`
   - `wrangler secret put PRIVATE_KEY_PEM` (ES256 PKCS#8 PEM)
3. Bind KV: set the correct namespace id in `worker/wrangler.toml` under `binding = "LICENSES"`.
4. Publish: `wrangler deploy` (run from `worker/` folder)
5. Configure Stripe Webhook to the Worker URL.

### App verification

- Place your ES256 public key at `GameCacheCleaner.UI/Assets/public.pem`.
- The app verifies license tokens offline via `ECDSA(P-256, SHA-256)` and gates Pro features.
- UI: `Buy Pro` opens Payment Link; `Enter License` stores token at `%ProgramData%\CrestPoint\GCC\license.json`.

## Installer & Release

- Inno Setup script: `installer/GameCacheCleaner.iss` (per‑user install, Start Menu entry, optional desktop shortcut).
- GitHub Actions: `.github/workflows/release.yml` attaches `GameCacheCleaner_Setup_v*.exe` to release tags (`v*`).

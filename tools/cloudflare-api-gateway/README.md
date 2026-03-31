# Tuvima API Gateway

A Cloudflare Worker that acts as a secure proxy to external APIs. The Engine sends requests through this gateway, which injects the required API keys on the fly — so sensitive credentials never live in the open-source codebase or on the user's machine.

**Production URL:** `https://tuvima-library-proxy.tuvima.workers.dev/`

## Supported Providers

| Provider | Auth Method | Secret Name |
|----------|-------------|-------------|
| Apple MusicKit | ES256 JWT (built on the fly) | `APPLE_KEY_ID`, `APPLE_TEAM_ID`, `APPLE_PRIVATE_KEY` |
| TMDB | Bearer token | `TMDB_API_TOKEN` |
| Google Books | Query parameter | `GOOGLE_API_KEY` |
| Fanart.tv | Query parameter | `FANART_TV_API_KEY` |

All requests require the `APP_PROXY_SECRET` shared secret.

## How It Works

The Engine sends an HTTP request with three headers:

| Header | Purpose |
|--------|---------|
| `x-proxy-secret` | Shared secret to authenticate the caller |
| `x-provider` | Which provider to inject credentials for (`apple`, `tmdb`, `google`, `fanart_tv`) |
| `x-target-url` | The actual API URL to call |

The gateway validates the secret, injects the provider's API key into the request, forwards it, and returns the response.

## Deployment

### Automatic (GitHub Actions)

Pushing changes to `tools/cloudflare-api-gateway/` on `main` triggers automatic deployment. This requires two GitHub repository secrets:

| GitHub Secret | Where to find it |
|---|---|
| `CLOUDFLARE_API_TOKEN` | [Cloudflare Dashboard → Profile → API Tokens](https://dash.cloudflare.com/profile/api-tokens) — use the "Edit Cloudflare Workers" template |
| `CLOUDFLARE_ACCOUNT_ID` | Cloudflare Dashboard → Workers & Pages → right sidebar |

### Manual

```bash
cd tools/cloudflare-api-gateway
npm install
npx wrangler login
npm run deploy
```

## Setting Worker Secrets

After the first deploy, set the shared proxy secret and any provider keys you need:

```bash
cd tools/cloudflare-api-gateway

# Required — shared secret for authenticating Engine requests
npx wrangler secret put APP_PROXY_SECRET

# TMDB
npx wrangler secret put TMDB_API_TOKEN

# Google Books
npx wrangler secret put GOOGLE_API_KEY

# Fanart.tv
npx wrangler secret put FANART_TV_API_KEY

# Apple MusicKit (when needed)
npx wrangler secret put APPLE_KEY_ID
npx wrangler secret put APPLE_TEAM_ID
cat AuthKey_YOURKID.p8 | npx wrangler secret put APPLE_PRIVATE_KEY
```

## Local Development

```bash
cd tools/cloudflare-api-gateway
npm install
npm start
```

This starts a local dev server via `wrangler dev`. You'll need to set secrets locally via `.dev.vars` (not committed):

```ini
APP_PROXY_SECRET=your-local-secret
TMDB_API_TOKEN=your-token
```

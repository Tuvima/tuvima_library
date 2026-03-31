// --- Apple MusicKit JWT (Native Web Crypto, zero dependencies) ---

function base64urlEncode(source) {
  let encoded = btoa(String.fromCharCode(...new Uint8Array(source)));
  return encoded.replace(/=/g, '').replace(/\+/g, '-').replace(/\//g, '_');
}

function stringToBuffer(str) {
  return new TextEncoder().encode(str);
}

function pemToArrayBuffer(pem) {
  const b64Lines = pem.replace(/(-----(BEGIN|END) PRIVATE KEY-----|\n|\r)/g, '');
  const binaryDerString = atob(b64Lines);
  const binaryDer = new Uint8Array(binaryDerString.length);
  for (let i = 0; i < binaryDerString.length; i++) {
    binaryDer[i] = binaryDerString.charCodeAt(i);
  }
  return binaryDer.buffer;
}

let appleCachedToken = null;
let appleTokenExpiration = 0;

async function getAppleDeveloperToken(env) {
  const now = Math.floor(Date.now() / 1000);
  if (appleCachedToken && appleTokenExpiration > now + 3600) return appleCachedToken;

  const exp = now + (30 * 24 * 60 * 60); // 30 days

  const header = { alg: "ES256", kid: env.APPLE_KEY_ID };
  const payload = { iss: env.APPLE_TEAM_ID, iat: now, exp: exp };

  const encodedHeader = base64urlEncode(stringToBuffer(JSON.stringify(header)));
  const encodedPayload = base64urlEncode(stringToBuffer(JSON.stringify(payload)));
  const dataToSign = `${encodedHeader}.${encodedPayload}`;

  const privateKeyBuffer = pemToArrayBuffer(env.APPLE_PRIVATE_KEY);

  const cryptoKey = await crypto.subtle.importKey(
    "pkcs8",
    privateKeyBuffer,
    { name: "ECDSA", namedCurve: "P-256" },
    false,
    ["sign"]
  );

  const signatureBuffer = await crypto.subtle.sign(
    { name: "ECDSA", hash: { name: "SHA-256" } },
    cryptoKey,
    stringToBuffer(dataToSign)
  );

  const encodedSignature = base64urlEncode(new Uint8Array(signatureBuffer));

  const jwt = `${dataToSign}.${encodedSignature}`;

  appleCachedToken = jwt;
  appleTokenExpiration = exp;
  return jwt;
}

// --- Request Handler ---

export default {
  async fetch(request, env, ctx) {
    // Handle CORS preflight
    if (request.method === 'OPTIONS') {
      return new Response(null, {
        status: 204,
        headers: {
          'Access-Control-Allow-Origin': '*',
          'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
          'Access-Control-Allow-Headers': 'Content-Type, x-target-url, x-provider, x-proxy-secret',
        },
      });
    }

    try {
      // 1. Authenticate the caller via shared secret
      const proxySecret = request.headers.get('x-proxy-secret');
      if (!env.APP_PROXY_SECRET || proxySecret !== env.APP_PROXY_SECRET) {
        return new Response('Unauthorized', { status: 403 });
      }

      // 2. Read routing headers
      const targetUrlString = request.headers.get('x-target-url');
      const provider = request.headers.get('x-provider');

      if (!targetUrlString || !provider) {
        return new Response('Missing x-target-url or x-provider header', { status: 400 });
      }

      // 3. Build the proxy request
      const targetUrl = new URL(targetUrlString);
      const proxyRequest = new Request(targetUrl, request);

      // Remove custom headers before forwarding
      proxyRequest.headers.delete('x-target-url');
      proxyRequest.headers.delete('x-provider');
      proxyRequest.headers.delete('x-proxy-secret');

      // 4. Inject secrets based on provider
      switch (provider.toLowerCase()) {
        case 'apple':
          const appleToken = await getAppleDeveloperToken(env);
          proxyRequest.headers.set('Authorization', `Bearer ${appleToken}`);
          break;

        case 'tmdb':
          if (env.TMDB_API_TOKEN) {
            proxyRequest.headers.set('Authorization', `Bearer ${env.TMDB_API_TOKEN}`);
          }
          break;

        case 'google':
          if (env.GOOGLE_API_KEY) {
            const updatedUrl = new URL(proxyRequest.url);
            updatedUrl.searchParams.append('key', env.GOOGLE_API_KEY);
            const response = await fetch(new Request(updatedUrl, proxyRequest));
            return corsResponse(response);
          }
          break;

        case 'fanart_tv':
          if (env.FANART_TV_API_KEY) {
            const updatedUrl = new URL(proxyRequest.url);
            updatedUrl.searchParams.append('api_key', env.FANART_TV_API_KEY);
            const response = await fetch(new Request(updatedUrl, proxyRequest));
            return corsResponse(response);
          }
          break;

        default:
          return new Response(`Unsupported provider: ${provider}`, { status: 400 });
      }

      // 5. Forward and return
      const response = await fetch(proxyRequest);
      return corsResponse(response);

    } catch (error) {
      return new Response('Gateway Error', { status: 502 });
    }
  },
};

function corsResponse(response) {
  const newResponse = new Response(response.body, response);
  newResponse.headers.set('Access-Control-Allow-Origin', '*');
  return newResponse;
}

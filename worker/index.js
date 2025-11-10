// Cloudflare Worker for Stripe licensing with KV binding LICENSES
// Endpoints:
// - POST /webhook/stripe: verify Stripe signature and mint license
// - GET /claim?session_id=...: return license token for a completed session
// - GET /health: check secrets and KV binding

// Helper: base64url encode/decode
function toBase64Url(bytes) {
  const b64 = btoa(String.fromCharCode(...new Uint8Array(bytes)));
  return b64.replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '');
}

function fromBase64Url(str) {
  let s = str.replace(/-/g, '+').replace(/_/g, '/');
  const pad = s.length % 4;
  if (pad) s += '='.repeat(4 - pad);
  const bin = atob(s);
  const bytes = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
  return bytes.buffer;
}

async function sha256(text) {
  const enc = new TextEncoder();
  const data = enc.encode(text);
  const hash = await crypto.subtle.digest('SHA-256', data);
  return new Uint8Array(hash);
}

// Import ES256 private key from PKCS#8 PEM
async function importPrivateKey(pem) {
  const pkcs8 = pem.replace(/-----BEGIN PRIVATE KEY-----/, '')
    .replace(/-----END PRIVATE KEY-----/, '')
    .replace(/\s+/g, '');
  const der = fromBase64Url(pkcs8.replace(/\+/g, '-').replace(/\//g, '_'));
  return crypto.subtle.importKey(
    'pkcs8',
    der,
    { name: 'ECDSA', namedCurve: 'P-256' },
    false,
    ['sign']
  );
}

async function signPayloadEcdsa(payloadBytes, pem) {
  const key = await importPrivateKey(pem);
  const sig = await crypto.subtle.sign({ name: 'ECDSA', hash: 'SHA-256' }, key, payloadBytes);
  return new Uint8Array(sig);
}

// Stripe webhook signature verification (v1)
async function verifyStripeSignature(req, signingSecret) {
  const sigHeader = req.headers.get('Stripe-Signature');
  if (!sigHeader) return false;
  // Header format: t=timestamp,v1=signature[,v0=...]
  const parts = Object.fromEntries(sigHeader.split(',').map(kv => {
    const [k, v] = kv.split('=');
    return [k.trim(), v.trim()];
  }));
  const timestamp = parts['t'];
  const v1 = parts['v1'];
  if (!timestamp || !v1) return false;
  const body = await req.text();
  const signedPayload = `${timestamp}.${body}`;
  const key = await crypto.subtle.importKey(
    'raw',
    new TextEncoder().encode(signingSecret),
    { name: 'HMAC', hash: 'SHA-256' },
    false,
    ['verify']
  );
  const sigBytes = new Uint8Array(Array.from(atob(v1), c => c.charCodeAt(0)));
  return crypto.subtle.verify('HMAC', key, sigBytes, new TextEncoder().encode(signedPayload));
}

function json(obj, status = 200) {
  return new Response(JSON.stringify(obj), {
    status,
    headers: { 'content-type': 'application/json' }
  });
}

export default {
  async fetch(req, env) {
    const url = new URL(req.url);
    const path = url.pathname;

    if (path === '/health') {
      const ok = !!(env.STRIPE_SECRET_KEY && env.WEBHOOK_SIGNING_SECRET && env.PRIVATE_KEY_PEM && env.LICENSES);
      return json({ ok }, ok ? 200 : 500);
    }

    if (path === '/claim') {
      const sid = url.searchParams.get('session_id');
      if (!sid) return new Response('missing session_id', { status: 400 });
      const token = await env.LICENSES.get(`sess:${sid}`);
      if (!token) return new Response('not found', { status: 404 });
      return new Response(token, { status: 200, headers: { 'content-type': 'text/plain' } });
    }

    if (path === '/webhook/stripe' && req.method === 'POST') {
      const ok = await verifyStripeSignature(req, env.WEBHOOK_SIGNING_SECRET);
      if (!ok) return new Response('bad signature', { status: 400 });
      const bodyText = await req.text();
      let evt;
      try { evt = JSON.parse(bodyText); } catch { return new Response('bad json', { status: 400 }); }
      if (evt.type !== 'checkout.session.completed') return json({ received: true });
      const sess = evt.data?.object || {};
      const sessionId = sess.id;
      const email = (sess.customer_details?.email || sess.customer_email || '').toString().toLowerCase();
      if (!sessionId) return new Response('missing session id', { status: 400 });

      const emailHashBytes = await sha256(email || '');
      const emailHash = toBase64Url(emailHashBytes);

      const licenseId = crypto.randomUUID();
      const payload = {
        licenseId,
        product: 'gcc-pro',
        seats: 1,
        emailHash,
        issuedAt: Math.floor(Date.now() / 1000)
      };
      const payloadStr = JSON.stringify(payload);
      const payloadBytes = new TextEncoder().encode(payloadStr);
      const sigBytes = await signPayloadEcdsa(payloadBytes, env.PRIVATE_KEY_PEM);
      const token = `${toBase64Url(payloadBytes)}.${toBase64Url(sigBytes)}`;

      await env.LICENSES.put(`sess:${sessionId}`, token);
      await env.LICENSES.put(`lic:${licenseId}`, token);
      return json({ ok: true, licenseId });
    }

    return new Response('not found', { status: 404 });
  }
};


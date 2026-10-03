// Deterministic stage-one wire contracts. Run from repository root: node docs/bridge/generate-contracts.mjs
import fs from 'node:fs/promises';
const out = new URL('./', import.meta.url);
const str = { type: 'string' };
const nullableString = { type: ['string', 'null'] };
const time = { type: 'string', format: 'date-time', pattern: 'Z$' };
const nullableTime = { ...time, type: ['string', 'null'] };
const nullableNumber = { type: ['number', 'null'] };
const ref = name => ({ $ref: `#/components/schemas/${name}` });
const obj = properties => ({ type: 'object', additionalProperties: false, required: Object.keys(properties), properties });
const schemas = {
  UsageWindow: obj({
    id: str, label: str, usedPercent: nullableNumber,
    remainingPercent: { ...nullableNumber, minimum: 0, maximum: 100 },
    windowMinutes: { type: ['integer', 'null'] }, resetAt: nullableTime
  }),
  UsageResult: obj({
    schemaVersion: { const: 1 }, bridgeId: str, providerId: { const: 'codex' }, accountId: str,
    displayName: str, status: { enum: ['OK', 'STALE', 'AUTH_REQUIRED', 'NO_DATA', 'NETWORK_ERROR', 'UNSUPPORTED'] },
    quotaWindows: { type: 'array', items: ref('UsageWindow') }, updatedAt: time,
    dataTimestamp: nullableTime, sourceTimestamp: nullableTime, isStale: { type: 'boolean' }, errorCode: nullableString
  }),
  Error: obj({ errorCode: str }),
  Health: obj({ schemaVersion: { const: 1 }, bridgeId: str, version: str, status: { enum: ['READY', 'PAUSED'] } }),
  Provider: obj({ providerId: { const: 'codex' }, capabilities: { type: 'array', items: { enum: ['usage', 'refresh'] }, uniqueItems: true } }),
  Account: obj({ providerId: { const: 'codex' }, accountId: str, displayName: str }),
  Invitation: obj({
    schemaVersion: { const: 1 }, bridgeId: str, endpoint: { type: 'string', format: 'uri', pattern: '^https://' },
    certificateSha256: { type: 'string', pattern: '^[A-Fa-f0-9]{64}$' }, pairToken: { type: 'string', minLength: 43 }, expiresAt: time
  }),
  PairRequest: obj({ pairToken: { type: 'string', minLength: 43 }, deviceName: { type: 'string', minLength: 1, maxLength: 80 } }),
  PairAccepted: obj({ pairId: str, sessionToken: { type: 'string', minLength: 43 }, state: { const: 'PendingApproval' }, expiresAt: time }),
  PairStatus: obj({
    pairId: str, state: { enum: ['PendingApproval', 'Approved', 'Delivered', 'Rejected'] },
    deviceId: nullableString, deviceToken: nullableString, expiresAt: time
  })
};
const json = value => JSON.stringify(value, null, 2) + '\n';
const response = (name, description) => ({ description, content: { 'application/json': { schema: ref(name) } } });
const err = (description, retry = false) => ({ ...response('Error', description), ...(retry ? { headers: { 'Retry-After': { description: 'Integer seconds until retry', schema: { type: 'integer', minimum: 1 } } } } : {}) });
const arrayResponse = (name, description, maxItems) => ({ description, content: { 'application/json': { schema: { type: 'array', items: ref(name), ...(maxItems != null ? { maxItems } : {}) } } } });
const accountParam = { name: 'id', in: 'path', required: true, schema: str, description: 'Bridge-local anonymous account ID, never a platform ID' };
const pairParam = { name: 'id', in: 'path', required: true, schema: str, description: 'Pairing session ID; session credential belongs only in Authorization header' };
const deviceSecurity = [{ deviceBearer: [] }];
const sessionSecurity = [{ pairSessionBearer: [] }];
const api = {
  openapi: '3.1.0', info: { title: 'Codex Launcher AI Usage Bridge', version: '1.0.0', description: 'Full v1 contract. The embedded HTTPS read/refresh and pairing modules are implemented; launcher UI activation and LAN discovery remain pending. HTTPS only; single Codex provider/current account.' },
  servers: [{ url: 'https://192.0.2.1:43189', description: 'Documentation-only synthetic address; use user-selected LAN address at runtime' }],
  security: deviceSecurity,
  paths: {
    '/v1/device': { get: { operationId: 'devicePage', security: [], description: 'Public shell: connection address, server certificate fingerprint, pairing state/form. No quota without device credential. Browser compares fingerprint manually (C-2).', responses: { 200: { description: 'Public HTML shell', content: { 'text/html': { schema: str } } } } } },
    '/v1/health': { get: { operationId: 'health', security: [], responses: { 200: response('Health', 'Service identity and state only; no accounts') } } },
    '/v1/pair': { post: { operationId: 'requestPairing', security: [], requestBody: { required: true, content: { 'application/json': { schema: ref('PairRequest') } } }, responses: {
      202: response('PairAccepted', 'Invitation atomically consumed; wait for computer approval'),
      400: err('PAIR_TOKEN_INVALID or DEVICE_NAME_INVALID'), 409: err('PAIR_TOKEN_INVALID, PAIR_TOKEN_REPLAYED, DEVICE_LIMIT_REACHED or PAIR_CAPACITY_REACHED'), 410: err('PAIR_EXPIRED'),
      413: err('REQUEST_TOO_LARGE (16 KiB)'), 429: err('PAIR_RATE_LIMITED (5/source/minute)', true), 503: err('PAIRING_UNAVAILABLE')
    } } },
    '/v1/pair/{id}': { get: { operationId: 'pairStatus', security: sessionSecurity, parameters: [pairParam], responses: {
      200: response('PairStatus', 'Approved may include deviceToken for up to 2 minutes. Delivered never includes cleartext token.'),
      401: err('PAIR_SESSION_INVALID'), 410: err('PAIR_EXPIRED'), 503: err('PAIRING_UNAVAILABLE')
    } } },
    '/v1/pair/{id}/ack': { post: { operationId: 'ackPairing', security: sessionSecurity, parameters: [pairParam], responses: {
      204: { description: 'Delivery acknowledged; device token cleartext erased; repeated ACK is idempotent within session lifetime' },
      401: err('PAIR_SESSION_INVALID'), 409: err('PAIR_NOT_APPROVED'), 410: err('PAIR_EXPIRED'), 503: err('PAIRING_UNAVAILABLE')
    } } },
    '/v1/providers': { get: { operationId: 'providers', responses: { 200: arrayResponse('Provider', 'Single Codex provider', 1), 401: err('DEVICE_INVALID'), 429: err('DEVICE_RATE_LIMITED', true) } } },
    '/v1/accounts': { get: { operationId: 'accounts', responses: { 200: arrayResponse('Account', '0 or 1 current authorized account; unknown identity returns []', 1),
      401: err('DEVICE_INVALID'), 403: err('ACCOUNT_REAUTHORIZATION_REQUIRED'), 429: err('DEVICE_RATE_LIMITED', true) } } },
    '/v1/accounts/{id}/usage': { get: { operationId: 'usage', parameters: [accountParam], description: 'Cache-only read, no upstream query. Missing values remain null; reset expiry never fabricates 100%.', responses: {
      200: response('UsageResult', 'Read authorized cached usage'), 401: err('DEVICE_INVALID'),
      403: err('ACCOUNT_REAUTHORIZATION_REQUIRED or ACCOUNT_UNIDENTIFIABLE'), 409: err('ACCOUNT_CHANGED'), 429: err('DEVICE_RATE_LIMITED', true)
    } } },
    '/v1/accounts/{id}/refresh': { post: { operationId: 'refresh', parameters: [accountParam], description: 'Return current UsageResult immediately; trigger shared manual refresh in background. Global minimum interval 10 seconds. HTTP disconnect never cancels upstream.', responses: {
      202: response('UsageResult', 'Current snapshot; use subsequent GET for updated data'), 401: err('DEVICE_INVALID'),
      403: err('ACCOUNT_REAUTHORIZATION_REQUIRED or ACCOUNT_UNIDENTIFIABLE'), 409: err('ACCOUNT_CHANGED'), 429: err('DEVICE_RATE_LIMITED', true)
    } } }
  },
  components: { securitySchemes: {
    deviceBearer: { type: 'http', scheme: 'bearer', description: '256-bit device credential. Only Authorization header. Server stores hash only.' },
    pairSessionBearer: { type: 'http', scheme: 'bearer', description: 'Dedicated random session credential, not invitation or device token. Only Authorization header.' }
  }, schemas }
};
const standalone = {
  $schema: 'https://json-schema.org/draft/2020-12/schema', $id: 'urn:codex-launcher:bridge:v1:usage',
  ...JSON.parse(JSON.stringify(schemas.UsageResult).replaceAll('#/components/schemas/', '#/$defs/')),
  $defs: { UsageWindow: schemas.UsageWindow }
};
const base = { schemaVersion: 1, bridgeId: '00000000-0000-4000-8000-000000000001', providerId: 'codex', accountId: '7'.repeat(64), displayName: 'Codex账号', status: 'OK', quotaWindows: [
  { id: 'codex:primary', label: '5 小时', usedPercent: 12.5, remainingPercent: 87.5, windowMinutes: 300, resetAt: '2026-10-02T12:00:00Z' },
  { id: 'codex_other:secondary', label: '窗口长度未知', usedPercent: null, remainingPercent: null, windowMinutes: null, resetAt: null }
], updatedAt: '2026-10-02T08:00:00Z', dataTimestamp: '2026-10-02T08:00:00Z', sourceTimestamp: null, isStale: false, errorCode: null };
await fs.mkdir(new URL('examples/', out), { recursive: true });
await fs.writeFile(new URL('openapi.json', out), json(api));
await fs.writeFile(new URL('usage-result.schema.json', out), json(standalone));
await fs.writeFile(new URL('examples/usage-ok.json', out), json(base));
await fs.writeFile(new URL('examples/usage-stale.json', out), json({ ...base, status: 'STALE', isStale: true, updatedAt: '2026-10-02T08:20:00Z', errorCode: 'UPSTREAM_NETWORK_ERROR' }));
for (const status of ['AUTH_REQUIRED', 'NO_DATA', 'NETWORK_ERROR', 'UNSUPPORTED']) {
  await fs.writeFile(new URL(`examples/usage-${status.toLowerCase().replaceAll('_','-')}.json`, out), json({ ...base, status, quotaWindows: [], dataTimestamp: null, sourceTimestamp: null, errorCode: status === 'NO_DATA' ? 'QUOTA_MONITORING_DISABLED' : null }));
}
console.log('Generated OpenAPI, JSON Schema and six synthetic responses');


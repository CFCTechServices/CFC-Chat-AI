import assert from 'node:assert/strict';
import http from 'node:http';
import { spawn, execFileSync } from 'node:child_process';
import { once } from 'node:events';
import { fileURLToPath } from 'node:url';
import { test } from 'node:test';

const script = fileURLToPath(new URL('./dotnet-edge.mjs', import.meta.url));

test('generated IIS config has deny-first routing, no Python fallback, and a bounded body size', () => {
  const xml = execFileSync(process.execPath, [script, '--iis'], { encoding: 'utf8' });
  assert.ok(xml.indexOf('name="Deny dev"') < xml.indexOf('name="API GET"'));
  assert.match(xml, /appendQueryString="true"/);
  assert.match(xml, /existingResponse="PassThrough"/);
  assert.match(xml, /maxAllowedContentLength="53477376"/);
  assert.match(xml, /url="ui\/index\.html"/);
  assert.doesNotMatch(xml, /8000/);
});

test('local same-origin edge passes approved traffic and rejects everything else', { timeout: 10000 }, async t => {
  const received = [];
  const upstream = http.createServer(async (req, res) => {
    let body = '';
    for await (const part of req) body += part;
    received.push({ url: req.url, method: req.method, headers: req.headers, body });
    res.writeHead(409, { 'content-type': 'application/json' }).end('{"detail":"mock response"}');
  });
  upstream.listen(5000, '127.0.0.1');
  await once(upstream, 'listening');
  t.after(() => upstream.close());
  const proxy = spawn(process.execPath, [script], { stdio: ['ignore', 'pipe', 'inherit'] });
  t.after(() => proxy.kill());
  await Promise.race([
    once(proxy.stdout, 'data'),
    once(proxy, 'exit').then(([code]) => { throw new Error(`Proxy exited: ${code}`); }),
  ]);

  // Native HTTP keeps request paths intact for the encoding checks.
  const request = (path, method = 'GET', body) => new Promise((resolve, reject) => {
    const req = http.request({ hostname: '127.0.0.1', port: 8080, path, method, agent: false, headers: {
      authorization: 'Bearer local-test', 'x-forwarded-for': '203.0.113.12', forwarded: 'for=203.0.113.12',
      ...(body ? { 'content-length': Buffer.byteLength(body), 'content-type': 'application/json' } : {}),
    } }, async res => {
      try {
        let text = '';
        for await (const part of res) text += part;
        resolve({ status: res.statusCode, headers: res.headers, text });
      } catch (error) { reject(error); }
    });
    req.on('error', reject).end(body);
  });

  const root = await request('/');
  assert.equal(root.status, 302);
  assert.equal(root.headers.location, '/ui/');
  assert.match((await request('/ui/')).text, /\/ui\/main\.jsx/);
  assert.equal((await request('/ui/main.jsx?v=dev2')).headers['content-type'], 'text/javascript');
  assert.equal((await request('/ui/styles.css', 'HEAD')).text, '');
  assert.match((await request('/reset-password')).text, /\/ui\/main\.jsx/);
  assert.match((await request('/chat')).text, /\/ui\/main\.jsx/);
  assert.match((await request('/docs')).text, /\/ui\/main\.jsx/);

  const body = '{"title":"New chat"}';
  const reply = await request('/api/chat/sessions?example=a%2Bb', 'POST', body);
  assert.equal(reply.status, 409);
  assert.equal(reply.headers['content-type'], 'application/json');
  assert.equal(reply.text, '{"detail":"mock response"}');
  assert.equal(received[0].url, '/api/chat/sessions?example=a%2Bb');
  assert.equal(received[0].method, 'POST');
  assert.equal(received[0].body, body);
  assert.equal(received[0].headers.authorization, 'Bearer local-test');
  assert.equal(received[0].headers['x-forwarded-for'], undefined);
  assert.equal(received[0].headers.forwarded, undefined);
  for (const image of ['/api/chat/content/images/a/caf%C3%A9.png', '/content/images/a/caf%C3%A9.png']) {
    assert.equal((await request(image)).status, 409);
    assert.equal(received.at(-1).url, image);
  }

  for (const [path, method] of [
    ['/api/profile/me', 'PATCH'], ['/api/admin/documents/doc/replace', 'PUT'],
    ['/api/admin/documents/doc', 'DELETE'],
  ]) {
    assert.equal((await request(path, method)).status, 409);
    assert.equal(received.at(-1).method, method);
    assert.equal(received.at(-1).url, path);
  }
  const approvedCount = received.length;

  for (const [path, method] of [
    ['/unknown'], ['/ui/missing.jsx'], ['/ui/not-a-page'], ['/api/missing'], ['/api/health', 'POST'],
    ['/api/videos/upload', 'POST'], ['/api/ingest/document', 'POST'], ['/api/health/detailed'],
    ['/api/ingest/dev', 'POST'], ['/API/Ingest/DEV/', 'POST'], ['/api/ingest/dev/a', 'DELETE'],
    ['/api/%69ngest/%64ev/a', 'DELETE'], ['/api/ingest%2Fdev', 'POST'],
    ['/api/ingest/%2564ev', 'POST'], ['/api/chat/../ingest/dev', 'POST'],
  ]) {
    const result = await request(path, method);
    assert.equal(result.status, 404, `${method ?? 'GET'} ${path}`);
    assert.doesNotMatch(result.text, /<html/i);
  }
  assert.equal(received.length, approvedCount);
});

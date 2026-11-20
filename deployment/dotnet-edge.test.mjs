import assert from 'node:assert/strict';
import http from 'node:http';
import { execFileSync } from 'node:child_process';
import { once } from 'node:events';
import { mkdtemp, readFile, readdir, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { test } from 'node:test';
import { runInNewContext } from 'node:vm';
import { createLocalEdge } from './dotnet-edge.mjs';

const script = fileURLToPath(new URL('./dotnet-edge.mjs', import.meta.url));

function bundleUrl(html) {
  assert.doesNotMatch(html, /babel|\.jsx\b/);
  const match = html.match(/<script defer src="(\/ui\/app-[a-f0-9]{16}\.js)"><\/script>/);
  assert.ok(match, 'expected a precompiled classic UI script');
  return match[1];
}

test('IIS staging emits only deployable assets and the classic bundle reaches React mount', async t => {
  const temp = await mkdtemp(join(tmpdir(), 'cfc-ui-'));
  t.after(() => rm(temp, { recursive: true, force: true }));
  const output = join(temp, 'ui');
  const builder = fileURLToPath(new URL('./build-ui.mjs', import.meta.url));
  execFileSync(process.execPath, [builder, output]);
  const html = await readFile(join(output, 'index.html'), 'utf8');
  const name = bundleUrl(html).slice('/ui/'.length);
  assert.deepEqual((await readdir(output)).sort(), [name, 'index.html', 'logo-cfc.png', 'styles.css']);

  const root = {};
  let rendered;
  runInNewContext(await readFile(join(output, name), 'utf8'), {
    window: { location: {} },
    document: { getElementById: id => { assert.equal(id, 'root'); return root; } },
    React: { createContext: () => ({}), createElement: component => ({ component }) },
    ReactDOM: { createRoot: element => {
      assert.equal(element, root);
      return { render: tree => { rendered = tree; } };
    } },
  });
  assert.equal(typeof rendered?.component, 'function');
});

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
  upstream.listen(0, '127.0.0.1');
  await once(upstream, 'listening');
  t.after(() => upstream.close());
  const proxy = await createLocalEdge(upstream.address().port);
  proxy.listen(0, '127.0.0.1');
  await once(proxy, 'listening');
  t.after(() => proxy.close());
  const { port } = proxy.address();

  // Native HTTP keeps request paths intact for the encoding checks.
  const request = (path, method = 'GET', body) => new Promise((resolve, reject) => {
    const req = http.request({ hostname: '127.0.0.1', port, path, method, agent: false, headers: {
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
  const entry = await request('/ui/');
  assert.equal(entry.status, 200);
  const bundle = await request(bundleUrl(entry.text));
  assert.equal(bundle.status, 200);
  assert.equal(bundle.headers['content-type'], 'text/javascript');
  assert.ok(bundle.text.length > 0);
  const css = await request('/ui/styles.css', 'HEAD');
  assert.equal(css.headers['content-type'], 'text/css');
  assert.equal(css.text, '');
  for (const page of ['/reset-password', '/chat', '/admin', '/docs']) {
    const reply = await request(page);
    assert.equal(reply.status, 200);
    assert.equal(reply.text, entry.text);
  }

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
    ['/unknown'], ['/ui/missing.js'], ['/ui/main.jsx?v=dev2'], ['/ui/not-a-page'], ['/api/missing'], ['/api/health', 'POST'],
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

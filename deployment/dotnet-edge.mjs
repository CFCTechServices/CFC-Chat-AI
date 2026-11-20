import http from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { resolve, sep } from 'node:path';
const routes = JSON.parse(await readFile(new URL('./routes-dotnet.manifest', import.meta.url)));
const patterns = Object.entries(routes).map(([method, paths]) => [method, `^(?:${paths.join('|')})/?$`]);
const deny = '^api/ingest/dev(?:/|$)';
// Keep in sync with the address paths written by web/state/router-context.jsx.
const uiPage = '^(?:chat|admin|docs|reset-password|settings|history)/?$';

function endToEndHeaders(headers) {
  const hopByHop = new Set(['connection', 'keep-alive', 'proxy-connection', 'proxy-authenticate',
    'proxy-authorization', 'te', 'trailer', 'transfer-encoding', 'upgrade',
    ...String(headers.connection || '').toLowerCase().split(',').map(name => name.trim())]);
  return Object.fromEntries(Object.entries(headers).filter(([name]) => !hopByHop.has(name)));
}
if (process.argv[2] === '--iis') {
  const proxy = patterns.map(([method, pattern]) => `<rule name="API ${method}" stopProcessing="true"><match url="${pattern}"/><conditions><add input="{REQUEST_METHOD}" pattern="^${method}$"/></conditions><action type="Rewrite" url="http://127.0.0.1:5000/{R:0}" appendQueryString="true"/></rule>`).join('\n');
  console.log(`<configuration><system.webServer>
<httpErrors existingResponse="PassThrough"/>
<staticContent><remove fileExtension=".jsx"/><mimeMap fileExtension=".jsx" mimeType="text/javascript"/></staticContent>
<defaultDocument enabled="true"><files><clear/><add value="index.html"/></files></defaultDocument>
<security><requestFiltering allowDoubleEscaping="false"><requestLimits maxAllowedContentLength="53477376"/></requestFiltering></security>
<rewrite><rules><clear/>
<rule name="Deny dev" stopProcessing="true"><match url="${deny}"/><action type="CustomResponse" statusCode="404" statusReason="Not Found"/></rule>
${proxy}
<rule name="Root" stopProcessing="true"><match url="^(?:ui)?$"/><conditions><add input="{REQUEST_METHOD}" pattern="^(GET|HEAD)$"/></conditions><action type="Redirect" url="/ui/" redirectType="Found"/></rule>
<rule name="UI page" stopProcessing="true"><match url="${uiPage}"/><conditions><add input="{REQUEST_METHOD}" pattern="^(GET|HEAD)$"/></conditions><action type="Rewrite" url="ui/index.html"/></rule>
<rule name="Only UI files" stopProcessing="true"><match url="^ui(?:/|$)" negate="true"/><action type="CustomResponse" statusCode="404" statusReason="Not Found"/></rule>
</rules></rewrite></system.webServer></configuration>`);
} else {
  const root = fileURLToPath(new URL('../web/', import.meta.url));
  const mime = { html: 'text/html', js: 'text/javascript', jsx: 'text/javascript', css: 'text/css', png: 'image/png' };
  const compiled = patterns.map(([method, pattern]) => [method, new RegExp(pattern, 'i')]);
  const blocked = new RegExp(deny, 'i');
  const pageRoute = new RegExp(uiPage, 'i');
  http.createServer(async (req, res) => {
    const error = (code) => res.writeHead(code, { 'content-type': 'text/plain' }).end(http.STATUS_CODES[code]);
    try {
      const raw = req.url.split('?')[0];
      const path = decodeURIComponent(raw);
      if (!path.startsWith('/') || /%2f/i.test(raw) || /[\\%\x00-\x1f]/.test(path) || path.split('/').some(x => x === '.' || x === '..')) return error(404);
      if (blocked.test(path.slice(1))) return error(404);
      if (compiled.some(([method, pattern]) => req.method === method && pattern.test(path.slice(1)))) {
        const headers = { ...endToEndHeaders(req.headers), host: '127.0.0.1:5000' };
        for (const key of Object.keys(headers)) if (key === 'forwarded' || key.startsWith('x-forwarded-')) delete headers[key];
        const upstream = http.request({ hostname: '127.0.0.1', port: 5000, method: req.method, path: req.url, headers }, reply => {
          res.writeHead(reply.statusCode, endToEndHeaders(reply.headers));
          reply.on('error', () => res.destroy());
          reply.pipe(res);
        });
        upstream.setTimeout(300_000, () => {
          upstream.destroy();
          if (res.headersSent) res.destroy(); else error(504);
        });
        upstream.on('error', () => {
          if (res.writableEnded || res.destroyed) return;
          if (res.headersSent) res.destroy(); else error(502);
        });
        res.on('close', () => upstream.destroy());
        req.pipe(upstream);
        return;
      }
      if (!['GET', 'HEAD'].includes(req.method)) return error(404);
      if (path === '/' || path === '/ui') return res.writeHead(302, { location: '/ui/' }).end();
      const page = pageRoute.test(path.slice(1));
      if (!page && !path.startsWith('/ui/')) return error(404);
      const file = resolve(root, page ? 'index.html' : path.slice(4) || 'index.html');
      const type = mime[file.split('.').at(-1)];
      if (!file.startsWith(resolve(root) + sep) || !type || !(await stat(file)).isFile()) return error(404);
      res.writeHead(200, { 'content-type': type });
      res.end(req.method === 'HEAD' ? undefined : await readFile(file));
    } catch { if (!res.headersSent) error(404); else res.destroy(); }
  }).listen(8080, '127.0.0.1', () => console.log('http://127.0.0.1:8080/ui/'));
}

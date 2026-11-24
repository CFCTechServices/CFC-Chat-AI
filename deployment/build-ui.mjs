import { createHash } from 'node:crypto';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { build } from 'esbuild';

const web = new URL('../web/', import.meta.url);

export async function buildUi() {
  // Keep the script order from the shared legacy entry page: these scripts
  // register providers/components on window.CFC before main.jsx mounts React.
  const source = await readFile(new URL('index.html', web), 'utf8');
  const entries = [];
  let html = source.replace(/^ +<script type="text\/babel" data-presets="env,react" src="\/ui\/([\w./-]+)(?:\?[^"]*)?"><\/script>\r?\n/gm, (_, path) => {
    entries.push(`import ${JSON.stringify(`./${path}`)};`);
    return '';
  }).replace(/^ +<script src="https:\/\/unpkg\.com\/@babel\/standalone@[^/]+\/babel\.min\.js"><\/script>\r?\n/m, '');
  if (!entries.length || /text\/babel|babel\.min\.js/.test(html)) {
    throw new Error('Unable to extract the legacy UI scripts from web/index.html');
  }

  const { outputFiles: [bundle] } = await build({
    stdin: { contents: entries.join('\n'), resolveDir: fileURLToPath(web), sourcefile: 'ui-entry.js' },
    bundle: true,
    write: false,
    format: 'iife',
    target: 'es2020',
    jsx: 'transform', // Classic React.createElement; React is loaded by the HTML.
    loader: { '.js': 'jsx' }, // The legacy chat-utils.js also contains JSX.
    minify: true,
  });
  const hash = createHash('sha256').update(bundle.contents).digest('hex').slice(0, 16);
  const name = `app-${hash}.js`;
  html = html.replace('</body>', `  <script defer src="/ui/${name}"></script>\n  </body>`);

  return new Map([
    ['index.html', html],
    [name, bundle.contents],
    ['styles.css', await readFile(new URL('styles.css', web))],
    ['logo-cfc.png', await readFile(new URL('logo-cfc.png', web))],
  ]);
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const outputPath = process.argv[2];
  if (!outputPath) throw new Error('Usage: node deployment/build-ui.mjs <new output directory>');
  const files = await buildUi();
  await mkdir(outputPath);
  await Promise.all([...files].map(([name, content]) => writeFile(join(outputPath, name), content)));
}

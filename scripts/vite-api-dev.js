/**
 * ローカル開発で api/ 以下の Vercel 関数を Vite の dev サーバー自身に動かさせる。
 *
 * `vercel dev` は関数を別プロセスで起動するが、この環境ではその生成が
 * spawn EBADF で失敗する。ここでは同じハンドラを同じプロセス内で呼ぶので、
 * 子プロセスを使わず `npm run dev` だけで /api/* が本番と同じ経路で動く。
 *
 * 対応するのは api/ の素直な形だけ。
 *   api/cutout.js          -> /api/cutout
 *   api/turn.js            -> /api/turn
 *   api/match/[action].js  -> /api/match/enter  （query.action = 'enter'）
 * 先頭が _ のファイル（api/_match/*）は部品なので経路にしない。
 * skip に渡した経路は server.proxy など別の担当に譲る。
 */
import { existsSync, readdirSync } from 'node:fs';
import { join } from 'node:path';

const API_DIR = 'api';

// 経路（['match','enter']）から、呼ぶべきファイルと [動的な部分] を探す。
// 実ファイル名を優先し、無ければ [param] の形にあてる。
function resolveRoute(dir, segments, params = {}) {
  const [head, ...rest] = segments;
  if (!head || head.startsWith('_')) return null;

  if (rest.length === 0) {
    const exact = join(dir, `${head}.js`);
    if (existsSync(exact)) return { file: exact, params };
    const dynamic = readdirSync(dir).find(name => /^\[.+\]\.js$/.test(name));
    if (dynamic) return { file: join(dir, dynamic), params: { ...params, [dynamic.slice(1, -4).replace(']', '')]: head } };
    return null;
  }

  const child = join(dir, head);
  if (existsSync(child)) return resolveRoute(child, rest, params);
  const dynamicDir = readdirSync(dir, { withFileTypes: true })
    .find(entry => entry.isDirectory() && /^\[.+\]$/.test(entry.name));
  if (!dynamicDir) return null;
  return resolveRoute(join(dir, dynamicDir.name), rest, { ...params, [dynamicDir.name.slice(1, -1)]: head });
}

const readBody = req => new Promise((resolve, reject) => {
  const chunks = [];
  req.on('data', chunk => chunks.push(chunk));
  req.on('end', () => resolve(Buffer.concat(chunks).toString('utf8')));
  req.on('error', reject);
});

// Vercel 関数が期待する res（status / json / setHeader）を node の res にかぶせる。
function wrapResponse(res) {
  res.status = code => { res.statusCode = code; return res; };
  res.json = data => { res.setHeader('Content-Type', 'application/json'); res.end(JSON.stringify(data)); return res; };
  res.send = data => { res.end(typeof data === 'string' ? data : JSON.stringify(data)); return res; };
  return res;
}

export function vercelApiDev({ env = {}, skip = [] } = {}) {
  return {
    name: 'vercel-api-dev',
    configureServer(server) {
      // VITE_ の付かない変数も関数から process.env で読めるようにする。
      // ブラウザには渡らない（この代入はサーバー側のプロセスだけ）。
      for (const [key, value] of Object.entries(env)) {
        if (process.env[key] === undefined) process.env[key] = value;
      }

      server.middlewares.use(async (req, res, next) => {
        const { pathname, searchParams } = new URL(req.url, 'http://localhost');
        if (!pathname.startsWith('/api/') || skip.includes(pathname)) return next();

        const route = resolveRoute(API_DIR, pathname.slice(5).split('/').filter(Boolean));
        if (!route) return next();

        try {
          // ssrLoadModule なので、ハンドラを直せば再起動なしで次の呼び出しに反映される。
          const module = await server.ssrLoadModule(`/${route.file}`);
          const raw = req.method === 'GET' || req.method === 'HEAD' ? '' : await readBody(req);
          req.query = { ...Object.fromEntries(searchParams), ...route.params };
          req.body = raw ? JSON.parse(raw) : {};
          await module.default(req, wrapResponse(res));
        } catch (error) {
          console.error(`[api] ${pathname}`, error);
          if (!res.headersSent) {
            res.statusCode = 500;
            res.setHeader('Content-Type', 'application/json');
            res.end(JSON.stringify({ error: String(error?.message ?? error) }));
          }
        }
      });
    },
  };
}

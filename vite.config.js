import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'
import { vercelApiDev } from './scripts/vite-api-dev.js'

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  // VITE_ が付かない変数も読む（ブラウザには渡らず、この設定ファイルの中だけで使う）
  const env = loadEnv(mode, process.cwd(), '')
  // 錬成APIの所在。本番の api/cutout.js と同じ API_URL を使う。
  // 未設定ならローカルの Python サーバー（uvicorn server:app --port 8000）を見る。
  const target = env.API_URL || 'http://127.0.0.1:8000'

  if (!env.API_URL) {
    console.warn(
      '\n[vite] API_URL が未設定です。剣の錬成は http://127.0.0.1:8000 へ転送されます。\n' +
      '       リモートのAPIを使う場合は .env.local に API_URL=（Cloud Run等のURL）を設定してください。\n'
    )
  }

  return {
    plugins: [
      react(),
      // ランダムマッチの待合所（api/match/[action].js）をこの dev サーバー自身に処理させる。
      // これが無いと /api/match/* は 404 になり、画面には「今は混み合っています」と出る。
      // 錬成（/api/cutout）は下の proxy が担当するので譲る。
      vercelApiDev({ env, skip: ['/api/cutout'] }),
    ],
    server: {
      // Cloudflare Tunnel 等の外部ホスト経由でアクセスするため許可する。
      // quick tunnel は起動ごとにサブドメインが変わるのでドメイン全体を指定する。
      allowedHosts: ['.trycloudflare.com'],
      proxy: {
        // ローカル開発用: /api/cutout → 錬成APIへ転送する。
        // 本番では Vercel の api/cutout.js が同じ経路を受け持つ（このプロキシは使われない）。
        '/api/cutout': {
          target,
          changeOrigin: true,
          rewrite: (path) => path.replace(/^\/api\/cutout/, '/cutout'),
          // APIキーはここで付ける。ブラウザには渡さない。
          headers: env.API_KEY ? { 'x-api-key': env.API_KEY } : {},
        },
      },
    },
  }
})

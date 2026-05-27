import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      // ローカル開発時: /api/cutout → http://127.0.0.1:8000/cutout に転送
      '/api/cutout': {
        target: 'http://127.0.0.1:8000',
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/api\/cutout/, '/cutout'),
      },
    },
  },
})

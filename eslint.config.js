import js from '@eslint/js'
import globals from 'globals'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import { defineConfig, globalIgnores } from 'eslint/config'

export default defineConfig([
  // 自分たちで書いていないもの（ビルド出力・Unityのビルド成果物・Unityのキャッシュ・Pythonの仮想環境）は見ない。
  // POSE_SWORD_Unity/Library は、Unity でプロジェクトを開くと各自の手元にできるキャッシュ（git には入らない）
  globalIgnores(['dist', 'public/POSE_SWORD_Unity', 'POSE_SWORD_Unity/Library', '**/.venv']),
  {
    // api/ は Vercel の Node 関数として動く
    files: ['api/**/*.js'],
    extends: [js.configs.recommended],
    languageOptions: { globals: globals.node },
  },
  {
    files: ['**/*.{js,jsx}'],
    extends: [
      js.configs.recommended,
      reactHooks.configs.flat.recommended,
      reactRefresh.configs.vite,
    ],
    languageOptions: {
      globals: globals.browser,
      parserOptions: { ecmaFeatures: { jsx: true } },
    },
  },
  {
    // Vite の設定と開発用スクリプトは Node で動く
    files: ['vite.config.js', 'scripts/**/*.js'],
    languageOptions: { globals: globals.node },
  },
])

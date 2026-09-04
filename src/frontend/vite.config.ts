import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import path from 'path'

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  const apiTarget =
    process.env.services__api__http__0 ||
    process.env.SERVICES__API__HTTP__0 ||
    process.env.services__api__https__0 ||
    process.env.SERVICES__API__HTTPS__0 ||
    env.VITE_API_URL ||
    'http://localhost:5029'

  return {
    plugins: [react(), tailwindcss()],
    resolve: {
      alias: {
        '@': path.resolve(import.meta.dirname, './src'),
      },
    },
    server: {
      port: process.env.PORT ? parseInt(process.env.PORT) : 5173,
      proxy: {
        '/api': {
          target: apiTarget,
          changeOrigin: true,
          secure: false,
        },
        '/hubs': {
          target: apiTarget,
          changeOrigin: true,
          secure: false,
          ws: true,
        },
        '/alive': {
          target: apiTarget,
          changeOrigin: true,
          secure: false,
        },
        '/health': {
          target: apiTarget,
          changeOrigin: true,
          secure: false,
        },
      },
    },
  }
})

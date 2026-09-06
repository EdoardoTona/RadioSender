import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  plugins: [vue()],
  base: '/flows/',
  build: { outDir: '../wwwroot/flows', emptyOutDir: true },
  server: {
    proxy: {
      '/api': {
        target: 'http://127.0.0.1:8082',
        changeOrigin: true,
        headers: { origin: 'http://127.0.0.1:8082' },
      },
      '/flowHub': {
        target: 'http://127.0.0.1:8082',
        ws: true,
        changeOrigin: true,
        headers: { origin: 'http://127.0.0.1:8082' },
      },
    },
  },
})

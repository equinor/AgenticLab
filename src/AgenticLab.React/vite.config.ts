import { readFileSync } from 'node:fs'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

const appVersion = readFileSync(new URL('../../version.txt', import.meta.url), 'utf8').trim()
if (!appVersion) throw new Error('version.txt must contain the application release version.')
const versionHeaders = { 'X-AgenticLab-Version': appVersion }

export default defineConfig({
  define: { 'import.meta.env.VITE_APP_VERSION': JSON.stringify(appVersion) },
  plugins: [react(), {
    name: 'application-version',
    transformIndexHtml: () => [{
      tag: 'meta',
      attrs: { name: 'application-version', content: appVersion },
      injectTo: 'head',
    }],
  }],
  server: {
    host: '127.0.0.1',
    port: Number(process.env.PORT ?? 5173),
    strictPort: true,
    headers: versionHeaders,
    proxy: {
      '/api': { target: process.env.BFF_URL ?? 'http://localhost:5181', changeOrigin: true, timeout: 0, proxyTimeout: 0 },
    },
  },
  preview: { headers: versionHeaders },
  test: { environment: 'jsdom', include: ['src/**/*.test.{ts,tsx}'], restoreMocks: true },
})

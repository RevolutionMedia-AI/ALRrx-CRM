import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    // COOP must be same-origin-allow-popups or unsafe-none for Google OAuth
    // implicit flow (popup) to work — strict same-origin blocks window.closed
    // calls from the popup, killing the login silently.
    headers: {
      'Cross-Origin-Opener-Policy': 'same-origin-allow-popups',
      'Cross-Origin-Embedder-Policy': 'unsafe-none',
    },
    proxy: {
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true
      },
      '/api/slice': {
        target: 'http://localhost:5100',
        changeOrigin: true,
        rewrite: (p) => p.replace(/^\/api\/slice/, '')
      },
      '/hubs': {
        target: 'http://localhost:5000',
        changeOrigin: true,
        ws: true
      }
    }
  }
})

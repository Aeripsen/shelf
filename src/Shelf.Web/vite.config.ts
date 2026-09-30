import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// In development the storefront runs on 5173 and forwards /api to the API on 5080.
// In Docker, nginx does the same forwarding (see nginx.conf).
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://localhost:5080',
    },
  },
})

import { fileURLToPath, URL } from 'node:url'
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// The ASP.NET backend (see backend/VideoCompression.Api/Properties/launchSettings.json).
const apiTarget = process.env.API_URL ?? 'http://localhost:5080'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    // In development the frontend calls /api/* on its own origin and Vite forwards it to the backend.
    proxy: {
      '/api': { target: apiTarget, changeOrigin: true },
    },
  },
  build: {
    // `npm run build` puts the app into the backend's wwwroot so one ASP.NET process serves everything.
    outDir: fileURLToPath(new URL('../backend/VideoCompression.Api/wwwroot', import.meta.url)),
    emptyOutDir: true,
  },
})

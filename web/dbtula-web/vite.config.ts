import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    proxy: {
      '/api': 'http://localhost:5275',
      '/hubs': { target: 'http://localhost:5275', ws: true },
    },
  },
  build: {
    rolldownOptions: {
      onwarn(warning, defaultHandler) {
        // Misplaced /*#__PURE__*/ annotation inside the SignalR dependency — not our code, harmless.
        if (warning.code === 'INVALID_ANNOTATION' && warning.message?.includes('@microsoft/signalr')) return
        defaultHandler(warning)
      },
    },
  },
})

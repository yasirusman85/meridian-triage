import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Builds into the ASP.NET Core app's wwwroot so one process serves everything.
export default defineConfig({
  plugins: [react()],
  build: {
    outDir: '../Meridian.Web/wwwroot',
    emptyOutDir: true,
  },
  server: {
    proxy: {
      '/api': 'http://localhost:5080',
    },
  },
});

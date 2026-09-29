import react from '@vitejs/plugin-react';
import { loadEnv } from 'vite';
import { defineConfig } from 'vitest/config';

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '');
  return {
    plugins: [react()],
    server: {
      proxy: {
        '/api': {
          target: env.VITE_API_PROXY_TARGET || 'http://127.0.0.1:7071',
          changeOrigin: true,
        },
      },
    },
    test: {
      pool: 'threads',
      maxWorkers: 1,
      environment: 'jsdom',
      setupFiles: ['./src/testSetup.ts'],
      include: ['src/**/*.test.ts', 'src/**/*.test.tsx'],
    },
  };
});

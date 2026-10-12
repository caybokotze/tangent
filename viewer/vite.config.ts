import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  base: './',
  build: { outDir: 'dist', sourcemap: true, chunkSizeWarningLimit: 1500 },
  server: {
    // The `.tan` TextMate grammar is imported from editors/vscode, outside this folder:
    // one grammar, two consumers.
    fs: { allow: ['..'] },
  },
  test: {
    globals: true,
    environment: 'node',
    include: ['src/**/*.test.ts', 'src/**/*.test.tsx'],
  },
});

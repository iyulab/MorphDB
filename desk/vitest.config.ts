import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import { resolve } from 'path'

export default defineConfig({
  plugins: [react()],
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/renderer/test/setup.ts'],
    include: ['src/**/*.{test,spec}.{ts,tsx}'],
    exclude: ['node_modules', 'out', 'dist'],
    coverage: {
      provider: 'v8',
      reporter: ['text', 'json', 'html'],
      exclude: [
        'node_modules/**',
        'out/**',
        'dist/**',
        '**/*.d.ts',
        '**/test/**',
        '**/*.config.{js,ts}'
      ]
    }
  },
  resolve: {
    alias: {
      '@': resolve(__dirname, './src/renderer'),
      '@main': resolve(__dirname, './src/main'),
      '@preload': resolve(__dirname, './src/preload')
    }
  }
})

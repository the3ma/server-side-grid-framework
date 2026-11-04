import { defineConfig } from 'vitest/config';

/** Unit tests for the pure grid modules (mapping, columns, errors, schemas, manifest, mock backend). */
export default defineConfig({
  test: {
    include: ['src/**/*.spec.ts'],
    environment: 'node',
  },
});

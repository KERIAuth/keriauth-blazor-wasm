import { defineConfig } from 'vitest/config';

export default defineConfig({
    test: {
        include: ['src/**/*.test.ts'],
        environment: 'node',
        globals: false,
        // Allow importing .js extensions (ES module compatibility)
        alias: {
            // Map .js imports to .ts source files for testing
        }
    },
    resolve: {
        // Vitest should resolve .js extensions to .ts files
        extensions: ['.ts', '.js']
    }
});

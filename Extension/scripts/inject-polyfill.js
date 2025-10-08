// inject-polyfill.js
// Post-build script to inject libsodium polyfill into BackgroundWorker.js
// This ensures polyfills run BEFORE signifyClient.js imports libsodium

import { readFileSync, writeFileSync } from 'fs';
import { fileURLToPath } from 'url';
import { dirname, join } from 'path';

const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);

// Path to the built BackgroundWorker.js
const backgroundWorkerPath = join(__dirname, '../bin/Debug/net9.0/browserextension/content/BackgroundWorker.js');

try {
    // Read the current BackgroundWorker.js
    let content = readFileSync(backgroundWorkerPath, 'utf8');

    // Check if polyfill is already injected
    if (content.includes('libsodium-polyfill.js')) {
        console.log('✓ Polyfill already injected in BackgroundWorker.js');
        process.exit(0);
    }

    // Find the first import statement
    const firstImportIndex = content.indexOf('import');

    if (firstImportIndex === -1) {
        console.error('✗ Could not find import statements in BackgroundWorker.js');
        process.exit(1);
    }

    // Inject the polyfill import at the very top
    const polyfillImport = 'import "/scripts/es6/libsodium-polyfill.js";\n';
    content = polyfillImport + content;

    // Write back
    writeFileSync(backgroundWorkerPath, content, 'utf8');
    console.log('✓ Successfully injected libsodium polyfill into BackgroundWorker.js');
} catch (error) {
    console.error('✗ Failed to inject polyfill:', error.message);
    process.exit(1);
}

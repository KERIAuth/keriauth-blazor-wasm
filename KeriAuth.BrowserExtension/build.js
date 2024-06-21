// build.js
// This file is used to build the ESBuild bundle
// Not used by other tsc-built files

import { strict } from 'assert';
import path from 'path'; // Use ES module syntax for path import
import { fileURLToPath } from 'url';

(async () => {
    const esbuild = (await import('esbuild')).default;
    const alias = (await import('esbuild-plugin-alias')).default;

    // Construct __dirname equivalent
    const __filename = fileURLToPath(import.meta.url);
    const __dirname = path.dirname(__filename);

    console.log('Building signify_ts_shim bundle...');
    esbuild.build({
        entryPoints: ['wwwroot/scripts/esbuild/signify_ts_shim.ts'],
        bundle: true,
        minify: true,
        outfile: 'wwwroot/scripts/esbuild/signify_ts_shim.js',
        platform: 'browser',
        format: 'esm',
        plugins: [
            alias({
                '@signify-ts': path.resolve(__dirname, 'node_modules/signify-ts/dist/signify-ts.mjs')
            })
        ],
        loader: {
            '.ts': 'ts'
        }
    }).catch((error) => {
        console.error(error);
        process.exit(1)
    });

    console.log('Building ContentScript bundle...');
    esbuild.build({
        entryPoints: ['wwwroot/scripts/esbuild/ContentScript.ts'],
        bundle: true,
        minify: true,
        outfile: 'wwwroot/scripts/esbuild/ContentScript.js',
        platform: 'browser',
        format: 'esm',
        loader: {
            '.ts': 'ts'
        }
    }).catch((error) => {
        console.error(error);
        process.exit(1)
    });
}) ();
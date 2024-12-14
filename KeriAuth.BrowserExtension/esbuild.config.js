// esbuild.config.js
import path from 'path';
import { fileURLToPath } from 'url';
import alias from 'esbuild-plugin-alias';
import esbuild from 'esbuild';

// Construct __dirname
const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

async function buildAll() {
    console.log('current directory: ', process.cwd());

    console.log('Building signify_ts_shim.js bundle...');
    await esbuild.build({
        entryPoints: ['wwwroot/scripts/esbuild/signify_ts_shim.ts'],
        bundle: true,
        minify: true,
        outfile: 'wwwroot/scripts/esbuild/signify_ts_shim.js',
        platform: 'browser',
        format: 'esm',
        plugins: [
            alias({
                '@signify-ts': path.resolve(__dirname, 'node_modules/signify-ts/dist/signify-ts.mjs'),
            })
        ],
        loader: { '.ts': 'ts' },
    });

    console.log('Building ContentScript.js bundle...');
    await esbuild.build({
        entryPoints: ['wwwroot/scripts/esbuild/ContentScript.ts'],
        bundle: true,
        minify: true,
        outfile: 'wwwroot/scripts/esbuild/ContentScript.js',
        platform: 'browser',
        format: 'esm',
        loader: { '.ts': 'ts' },
    });

    console.log('Building service-worker.js bundle...');
    await esbuild.build({
        entryPoints: ['wwwroot/scripts/esbuild/service-worker.ts'],
        bundle: true,
        minify: true,
        outfile: 'wwwroot/scripts/esbuild/service-worker.js',
        platform: 'browser',
        format: 'esm',
        loader: { '.ts': 'ts' },
    });

    console.log('All builds completed successfully.');
}

buildAll().catch(err => {
    console.error('Build failed:', err);
    process.exit(1);
});
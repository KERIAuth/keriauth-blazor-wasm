// build.js
// This file is used to build the ESBuild bundle(s)
// Not used by other tsc-built files

import { strict } from 'assert';
import path from 'path'; // Use ES module syntax for path import
import { fileURLToPath } from 'url';
import fs from 'fs';

(async () => {
    console.log('Current working directory:', process.cwd());
    console.log('Environment variables:', process.env);

    const esbuild = (await import('esbuild')).default;
    const alias = (await import('esbuild-plugin-alias')).default;

    // Construct __dirname equivalent
    const __filename = fileURLToPath(import.meta.url);
    const __dirname = path.dirname(__filename);

    try {
        console.log('current directory: ', process.cwd());
        console.log('Building signify_ts_shim.js bundle...');
        console.log('Entry point absolute path:', path.resolve(process.cwd(), 'wwwroot/scripts/esbuild/signify_ts_shim.ts'));
        console.log('Output file absolute path:', path.resolve(process.cwd(), 'wwwroot/scripts/esbuild/signify_ts_shim.js'));

        // Ensure the output directory exists
        const outputDir = path.resolve(process.cwd(), 'wwwroot/scripts/esbuild');
        if (!fs.existsSync(outputDir)) {
            console.error(`Output directory does not exist: ${outputDir}`);
        } else {
            console.log(`Output directory exists: ${outputDir}`);
        }

        // Ensure write permissions
        try {
            fs.accessSync(outputDir, fs.constants.W_OK);
            console.log(`Write permissions confirmed for directory: ${outputDir}`);
        } catch (err) {
            console.error(`No write permissions for directory: ${outputDir}`, err);
        }

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
            loader: {
                '.ts': 'ts'
            }
        });

        console.log('Building ContentScript.js bundle...');
        await esbuild.build({
            entryPoints: ['wwwroot/scripts/esbuild/ContentScript.ts'],
            bundle: true,
            minify: true,
            outfile: 'wwwroot/scripts/esbuild/ContentScript.js',
            platform: 'browser',
            format: 'esm',
            loader: {
                '.ts': 'ts'
            }
        });

        console.log('Building service-worker.js bundle...');
        await esbuild.build({
            entryPoints: ['wwwroot/scripts/esbuild/service-worker.ts'],
            bundle: true,
            minify: true,
            outfile: 'wwwroot/scripts/esbuild/service-worker.js',
            platform: 'browser',
            format: 'esm',
            loader: {
                '.ts': 'ts'
            }
        });

        console.log('All builds completed successfully.');

    } catch (error) {
        console.error('Build failed:', error);
        process.exit(1);
    }
})();

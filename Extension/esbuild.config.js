// esbuild.config.js
import path from 'path';
import { fileURLToPath } from 'url';
import alias from 'esbuild-plugin-alias';
import esbuild from 'esbuild';
import { execSync } from 'child_process';

// Construct __dirname
const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// Function to run TypeScript type checking
function runTypeCheck() {
    console.log('Running TypeScript type checking...');
    try {
        execSync('npx tsc --noEmit', { stdio: 'inherit' });
        console.log('✓ Type checking passed');
        return true;
    } catch (error) {
        console.error('✗ Type checking failed');
        return false;
    }
}

// Shared build options
const sharedOptions = {
    bundle: true,
    minify: process.env.NODE_ENV === 'production',
    sourcemap: true,
    platform: 'browser',
    format: 'iife',  // Changed from 'esm' to 'iife' for content scripts
    loader: { '.ts': 'ts' },
    define: {
        'process.env.NODE_ENV': JSON.stringify(process.env.NODE_ENV || 'development')
    }
};

// Build configurations
const builds = [
    {
        name: 'signifyClient',
        entryPoints: ['wwwroot/scripts/esbuild/signifyClient.ts'],
        outfile: 'dist/wwwroot/scripts/esbuild/signifyClient.js',
        platform: "browser",
        format: 'esm',  // Keep ESM for C# interop via import()
        plugins: [
            alias({
                '@signify-ts': path.resolve(__dirname, 'node_modules/signify-ts/dist/signify-ts.mjs'),
            })
        ]
    },
    {
        name: 'ContentScript',
        entryPoints: ['wwwroot/scripts/esbuild/ContentScript.ts'],
        outfile: 'dist/wwwroot/scripts/esbuild/ContentScript.js'
        // Will use IIFE format from sharedOptions
    }
];

async function buildAll() {
    console.log('Starting build process...');
    console.log(`Environment: ${process.env.NODE_ENV || 'development'}`);
    console.log(`Current directory: ${process.cwd()}`);
    
    // Run type checking first (unless in watch mode)
    if (process.argv[2] !== '--watch') {
        const typeCheckPassed = runTypeCheck();
        if (!typeCheckPassed && process.env.NODE_ENV === 'production') {
            console.error('Build aborted due to type errors');
            process.exit(1);
        }
    }
    
    // Perform builds
    for (const build of builds) {
        console.log(`\nBuilding ${build.name}...`);
        try {
            if (process.argv[2] === '--watch') {
                // Watch mode
                const { name, ...buildOptions } = build;
                const ctx = await esbuild.context({
                    ...sharedOptions,
                    ...buildOptions,
                    banner: {
                        js: `// ${name} - Built at ${new Date().toISOString()}`
                    }
                });
                await ctx.watch();
                console.log(`✓ Watching ${name}`);
            } else {
                // Regular build
                const { name, ...buildOptions } = build;
                await esbuild.build({
                    ...sharedOptions,
                    ...buildOptions,
                    banner: {
                        js: `// ${name} - Built at ${new Date().toISOString()}`
                    }
                });
                console.log(`✓ Built ${name}`);
            }
        } catch (error) {
            console.error(`✗ Failed to build ${build.name}:`, error);
            if (process.env.NODE_ENV === 'production') {
                process.exit(1);
            }
        }
    }
    
    if (process.argv[2] === '--watch') {
        console.log('\n👀 Watching for changes... Press Ctrl+C to stop');
    } else {
        console.log('\n✅ All builds completed successfully');
    }
}

// Handle errors
process.on('unhandledRejection', (error) => {
    console.error('Unhandled promise rejection:', error);
    process.exit(1);
});

// Run the build
buildAll().catch(err => {
    console.error('Build failed:', err);
    process.exit(1);
});
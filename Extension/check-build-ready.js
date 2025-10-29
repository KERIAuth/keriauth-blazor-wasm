#!/usr/bin/env node
/**
 * Pre-build check script for KERI Auth Browser Extension
 * Verifies that TypeScript has been compiled before dotnet build packages the extension
 *
 * Usage: node check-build-ready.js
 * Exit codes:
 *   0 - Build artifacts are ready
 *   1 - Build artifacts missing or stale
 */

import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// Critical files that must exist in dist/ after TypeScript build
const requiredDistFiles = [
    'dist/wwwroot/scripts/esbuild/signifyClient.js',
    'dist/wwwroot/scripts/esbuild/ContentScript.js',
    'dist/wwwroot/scripts/esbuild/demo1.js',
    'dist/wwwroot/scripts/esbuild/utils.js',
    'dist/wwwroot/scripts/es6/storageHelper.js'
];

// TypeScript source files (to check if dist is stale)
const sourceFiles = [
    'wwwroot/scripts/esbuild/signifyClient.ts',
    'wwwroot/scripts/esbuild/ContentScript.ts',
    'wwwroot/scripts/esbuild/demo1.ts',
    'wwwroot/scripts/esbuild/utils.ts'
];

function fileExists(filePath) {
    try {
        return fs.existsSync(path.join(__dirname, filePath));
    } catch {
        return false;
    }
}

function getFileModifiedTime(filePath) {
    try {
        const stats = fs.statSync(path.join(__dirname, filePath));
        return stats.mtime.getTime();
    } catch {
        return 0;
    }
}

function checkBuildArtifacts() {
    const errors = [];
    const warnings = [];

    // Check if node_modules exists
    if (!fileExists('node_modules')) {
        errors.push('node_modules/ directory not found. Run: npm install');
    }

    // Check if required dist files exist
    for (const file of requiredDistFiles) {
        if (!fileExists(file)) {
            errors.push(`Missing build artifact: ${file}`);
        }
    }

    // If dist files exist, check if they're stale
    if (errors.length === 0) {
        const newestSource = Math.max(...sourceFiles.map(getFileModifiedTime));
        const oldestDist = Math.min(...requiredDistFiles.map(getFileModifiedTime));

        if (newestSource > oldestDist) {
            warnings.push('TypeScript source files are newer than compiled output');
            warnings.push('Run: npm run build');
        }
    }

    return { errors, warnings };
}

function main() {
    console.log('🔍 Checking build artifacts...\n');

    const { errors, warnings } = checkBuildArtifacts();

    if (warnings.length > 0) {
        console.log('⚠️  Warnings:');
        warnings.forEach(w => console.log(`   ${w}`));
        console.log();
    }

    if (errors.length > 0) {
        console.log('❌ Errors:');
        errors.forEach(e => console.log(`   ${e}`));
        console.log();
        console.log('Build artifacts are not ready. Run:');
        console.log('   npm install && npm run build');
        console.log();
        process.exit(1);
    }

    if (warnings.length > 0) {
        console.log('⚠️  TypeScript build may be stale');
        console.log('Continuing anyway (use -p:FullBuild=true to rebuild TypeScript)');
        console.log();
        process.exit(0);
    }

    console.log('✅ Build artifacts are ready');
    console.log();
    process.exit(0);
}

main();

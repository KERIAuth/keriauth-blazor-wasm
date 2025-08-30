# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build/Run Commands

- Build: `dotnet build`
- Build with frontend: `npm install && npm run build && dotnet build`
- Test: `dotnet test`
- Run single test: `dotnet test --filter "FullyQualifiedName=Extension.Tests.Helper.UrlBuilderTests.CreateUrlWithEncodedQueryStrings_EncodesQueryParamsCorrectly"`
- Frontend build: `npm run build`
- ES6 TypeScript build: `npm run build:es6`
- Bundle with esbuild: `npm run bundle:esbuild`
- Clean build: `dotnet clean && npm run build && dotnet build`

## Development Environment

Primary development environment is Windows running WSL2. Commands above work in both Windows PowerShell/Command Prompt and WSL2 bash. Claude Code operates within the WSL2 environment.

## Project Structure

This is a KERI Auth browser extension built with:
- Blazor WebAssembly (.NET 9.0) for the extension UI
- TypeScript for content scripts and service worker
- MudBlazor for UI components
- signify-ts for KERI/ACDC operations
- polaris-web for web page communication protocol

Key project layout:
- `Extension/` - Main browser extension project (Blazor WASM)
- `Extension.Tests/` - xUnit test project
- `Extension/Services/` - Core services including SignifyService for KERI operations
- `Extension/UI/` - Blazor pages and components
- `Extension/wwwroot/scripts/` - TypeScript/JavaScript code
  - `es6/` - TypeScript modules compiled to ES6
  - `esbuild/` - Bundled scripts for service worker and content script

## Code Style Guidelines

- .NET 9.0 SDK required with rollForward: latestMinor (see global.json)
- 4-space indentation
- Opening braces on same line
- PascalCase for classes, interfaces (with 'I' prefix), public methods and properties
- Private fields: camelCase with underscore prefix (_fieldName)
- Constants: use readonly fields or const values (prefer const for immutable values)
- Nullable reference types enabled - use explicit null checks
- Use expression-bodied members for simple methods
- Try/catch with specific exception types
- Organize imports at top of file, outside namespace declarations
- Test pattern: Arrange-Act-Assert with descriptive test names using xUnit
- EnforceCodeStyleInBuild enabled in project files
- Async methods suffixed with "Async" returning Task/Task&lt;T&gt;
- XML comments on public APIs
- Use FluentResults for operation results (Result&lt;T&gt; pattern)

## Architecture Notes
The extension follows a multi-component architecture:
1. **Service Worker** - Background script handling extension lifecycle and message routing
2. **Content Script** - Injected into web pages, bridges page and extension communication
3. **Blazor WASM App** - UI layer for extension popup and tabs
4. **SignifyService** - Manages KERI/ACDC operations via signify-ts JavaScript interop

Services use dependency injection with primarily singleton lifetime for state management across the extension. The StorageService provides persistent storage using chrome.storage API.
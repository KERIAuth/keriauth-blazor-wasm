# Quick Start Guide

## 🚀 First Time Setup

```bash
# 1. Clone and enter directory
cd kbw

# 2. Install dependencies
cd Extension && npm install && cd ..

# 3. Build everything
dotnet build -p:FullBuild=true
```

## 📦 Load Extension in Browser

1. Open Chrome/Edge/Brave
2. Navigate to `chrome://extensions`
3. Enable "Developer mode" (top right)
4. Click "Load unpacked"
5. Select: `Extension/bin/Debug/net9.0/browserextension/`

## 🔨 Daily Development

### Making TypeScript Changes

```bash
# Option 1: Watch mode (auto-rebuild)
cd Extension && npm run watch

# Option 2: Manual rebuild
cd Extension && npm run build

# Then reload extension in browser (chrome://extensions)
```

### Making C# Changes

```bash
# Quick build (C# only)
dotnet build -p:Quick=true

# Then reload extension in browser
```

### Making Both TypeScript + C# Changes

```bash
# Full rebuild
dotnet build -p:FullBuild=true

# Then reload extension in browser
```

## 🐛 Debugging

### View Extension Logs

1. Open browser DevTools (F12)
2. Go to "Console" tab
3. Filter by "Extension"

### View Service Worker Logs

1. Go to `chrome://extensions`
2. Find "KERI Auth" extension
3. Click "service worker" link
4. Opens dedicated DevTools for background script

### Debug Blazor Code

1. Launch extension in browser
2. Open extension popup
3. Right-click → Inspect
4. DevTools opens
5. Sources tab → see Blazor C# code

## ✅ Testing

```bash
# Run all tests
dotnet test

# Run specific test
dotnet test --filter "ClassName=SignifyServiceTests"

# Lint TypeScript
cd Extension && npm run lint
```

## 🔧 Common Commands

| Task | Command |
|------|---------|
| **Full build** | `dotnet build -p:FullBuild=true` |
| **Quick build** | `dotnet build -p:Quick=true` |
| **Clean build** | `dotnet clean && dotnet build -p:FullBuild=true` |
| **Watch TypeScript** | `cd Extension && npm run watch` |
| **Run tests** | `dotnet test` |
| **Format code** | `dotnet format` |
| **Lint TypeScript** | `cd Extension && npm run lint` |

## 🆘 Something Broke?

### Nuclear Option (Clean Everything)

```bash
# Close Visual Studio first!
dotnet clean
cd Extension
rm -rf node_modules dist
npm install
npm run build
cd ..
dotnet build -p:FullBuild=true
```

### Extension Not Loading?

1. Check manifest.json exists in output:
   ```bash
   cat Extension/bin/Debug/net9.0/browserextension/manifest.json
   ```

2. Check JavaScript files exist:
   ```bash
   ls Extension/bin/Debug/net9.0/browserextension/scripts/esbuild/
   ```

3. Rebuild with verbose logging:
   ```bash
   dotnet build -p:FullBuild=true -v:detailed
   ```

### Changes Not Appearing?

1. Rebuild:
   ```bash
   dotnet build -p:FullBuild=true
   ```

2. **Hard reload extension** in browser:
   - Go to `chrome://extensions`
   - Click reload button (circular arrow)
   - NOT just refreshing the extension popup!

## 📚 More Help

- [BUILD_GUIDE.md](./BUILD_GUIDE.md) - Comprehensive build documentation
- [CLAUDE.md](./CLAUDE.md) - Full architecture and coding guidelines
- Check build system: `node Extension/check-build-ready.js`

## 🎯 VS Code Users

Press `Ctrl+Shift+B` to see build tasks:
- **Build Extension (Full)** - TypeScript + C# (default)
- **Build Extension (Quick)** - C# only
- **Watch TypeScript** - Auto-rebuild on changes
- **Test** - Run all tests

## 🎯 Visual Studio Users

**Before opening Visual Studio:**
- Close any WSL terminals running `npm watch`
- Close VS Code if open

**Building in Visual Studio:**
1. Right-click solution → Build Solution
2. For TypeScript changes:
   ```powershell
   # In Package Manager Console
   cd Extension
   npm run build
   # Then rebuild solution
   ```

## 🔑 Environment Requirements

- Node.js >= 22.13.0
- npm >= 11.0.0
- .NET 9.0 SDK
- Chrome/Edge/Brave (version 127+)

Check versions:
```bash
node --version
npm --version
dotnet --version
```

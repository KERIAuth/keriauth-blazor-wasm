# Makefile for KERI Auth browser extension
# Builds exclusively in WSL (Ubuntu). Windows builds are not supported.
#
# Targets:
#   make install       Install npm dependencies
#   make build-ts      Build TypeScript (scripts + app.ts)
#   make build         Full build (TypeScript + C#)
#   make test          Run C# tests
#   make clean         Clean all build artifacts
#   make clean-build   Full clean + install + build + test
#   make watch         Watch mode for TypeScript development
#   make typecheck     TypeScript type checking only (no emit)
#   make verify        Verify build output integrity
#   make prereqs       Check prerequisite tool versions

.PHONY: install build-ts build test clean clean-build watch typecheck verify prereqs

SHELL := /bin/bash

# Source nvm before each recipe line that needs node/npm.
# .nvmrc in the project root pins the required Node.js version.
# On CI (GitHub Actions sets CI=true), Node.js is on PATH via actions/setup-node — skip nvm.
NVM_USE := if [ -z "$$CI" ] && [ -f "$$HOME/.nvm/nvm.sh" ]; then source "$$HOME/.nvm/nvm.sh" --no-use && nvm use --silent; fi &&

# Configuration
CONFIGURATION := Release
DOTNET_BUILD_FLAGS := --configuration $(CONFIGURATION) -p:Quick=true
EXTENSION_OUTPUT := Extension/bin/$(CONFIGURATION)/net9.0/browserextension

prereqs:
	@echo "=== Checking prerequisites ==="
	@$(NVM_USE) node --version
	@$(NVM_USE) npm --version
	@dotnet --version

install:
	@echo "=== Installing npm dependencies ==="
	$(NVM_USE) cd scripts && npm install
	$(NVM_USE) cd Extension && npm install

build-ts:
	@echo "=== Building TypeScript ==="
	$(NVM_USE) cd scripts && npm run build
	$(NVM_USE) cd Extension && npm run build:app

build: build-ts
	@echo "=== Building C# ($(CONFIGURATION)) ==="
	dotnet build $(DOTNET_BUILD_FLAGS)

test:
	@echo "=== Running tests ==="
	dotnet test --configuration $(CONFIGURATION) --no-build --verbosity normal

verify:
	@echo "=== Verifying build output ==="
	@test -f Extension/wwwroot/scripts/esbuild/signifyClient.js || (echo "FAILED: Missing signifyClient.js" && exit 1)
	@test -f Extension/wwwroot/app.js || (echo "FAILED: Missing app.js" && exit 1)
	@test -f $(EXTENSION_OUTPUT)/manifest.json || (echo "FAILED: Missing manifest.json in build output" && exit 1)
	@echo "Build output verified."

clean:
	@echo "=== Cleaning build artifacts ==="
	$(NVM_USE) cd scripts && npm run clean
	$(NVM_USE) cd Extension && npm run clean
	rm -rf Extension/bin Extension/obj Extension.Tests/bin Extension.Tests/obj tsconfig.tsbuildinfo Extension.Tests/packages.lock.json.bak
	@echo "Note: node_modules/ directories were not removed. To remove them: rm -rf Extension/node_modules scripts/node_modules"

clean-build: prereqs clean
	@echo "=== Clean build ==="
	dotnet nuget locals all --clear
	dotnet restore -p:Configuration=$(CONFIGURATION) --force-evaluate
	$(MAKE) install
	$(MAKE) build
	$(MAKE) verify
	$(MAKE) test
	@echo "=== Clean build succeeded ==="
	@echo "Extension ready at: $(EXTENSION_OUTPUT)/"

watch:
	@echo "=== Starting watch mode ==="
	$(NVM_USE) cd scripts && npm run watch

typecheck:
	@echo "=== Type checking ==="
	$(NVM_USE) cd scripts && npm run typecheck
	$(NVM_USE) cd Extension && npm run typecheck

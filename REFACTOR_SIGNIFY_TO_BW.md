# Refactor SignifyClientService from App Context to BackgroundWorker

## Overview

This document tracks the refactoring of `SignifyClientService` interactions from the App WASM context to use request/response messaging with the BackgroundWorker. This ensures all KERIA/signify-ts operations go through the BackgroundWorker, which is the proper architectural boundary.

## Current State

Files with active `signifyClientService` method calls in App context:

| File | Line | Method | Status |
|------|------|--------|--------|
| DashboardPage.razor | 52 | `TestAsync()` | Phase 0 |
| DashboardPage.razor | 72 | `GetCredentials()` | Phase 6 |
| ConfigurePage.razor | 391 | `HealthCheck()` | Phase 1 |
| ConfigurePage.razor | 442 | `Connect()` | Phase 2 |
| ConfigurePage.razor | 511 | `GetIdentifiers()` | Phase 4 |
| ConfigurePage.razor | 525 | `RunCreateAid()` | Phase 3 |
| ConfigurePage.razor | 533 | `GetIdentifiers()` | Phase 4 |
| CredentialsPage.razor | 54 | `GetCredentials()` | Phase 6 |
| ConnectingPage.razor | 85 | `Connect()` | Phase 2 |
| ConnectingPage.razor | 108 | `GetIdentifiers()` | Phase 4 |
| ProfilePage.razor | 106 | `GetIdentifier()` | Phase 5 |
| ProfilePage.razor | 140 | `GetKeyState()` | Phase 7 (TODO) |
| ProfilePage.razor | 162 | `GetKeyEvents()` | Phase 7 (TODO) |
| ProfilePage.razor | 394 | `RenameAid()` | Phase 7 (TODO) |
| WebsiteConfigDisplay.razor | 257 | `GetCredentials()` | Phase 6 |

Files that inject but don't use `ISignifyClientService` (cleanup after all phases):
- UnlockPage.razor, WelcomePage.razor, GettingStartedPage.razor, KeriAgentServicePage.razor
- Passkeys.razor, AddPasskeyPage.razor, NewReleasePage.razor
- WebsitesPage.razor, WebsitePage.razor, MainLayout.razor, BaseLayout.razor

## Phases

### Phase 0: Remove TestAsync() in DashboardPage
- [x] Started
- [x] Complete
- **Location**: DashboardPage.razor:52
- **Action**: Remove the `TestAsync()` call and related logging (already marked as TODO P2)
- **Test**: Build succeeds, DashboardPage loads without errors

### Phase 1: HealthCheck() via BackgroundWorker
- [x] Started
- [x] Complete
- **Location**: ConfigurePage.razor:391
- **Changes**:
  - Add `HEALTH_CHECK_REQ` / `HEALTH_CHECK_RES` message types
  - Add BW handler in BackgroundWorker.cs
  - ConfigurePage sends RPC request via AppPortService
- **Test**: Configure page health check works, verify via BW logs

### Phase 2: Connect() via BackgroundWorker
- [x] Started
- [x] Complete
- **Locations**: ConfigurePage.razor:442, ConnectingPage.razor:85
- **Changes**:
  - Add `CONNECT_KERIA_REQ` / `CONNECT_KERIA_RES` message types
  - Add BW handler (BW already has `TryConnectSignifyClient`)
  - Update both pages to use port messaging
- **Note**: ConnectingPage is the normal unlock flow - critical path
- **Test**: Unlock flow works, ConfigurePage connect works

### Phase 3: RunCreateAid() via BackgroundWorker
- [x] Started
- [x] Complete
- **Location**: ConfigurePage.razor:525
- **Changes**:
  - Add `CREATE_AID_REQ` / `CREATE_AID_RES` message types
  - Add BW handler (BW already has similar logic)
- **Dependency**: Requires Phase 2 (must be connected)
- **Test**: Create AID from ConfigurePage works

### Phase 4: GetIdentifiers() from Storage
- [x] Started
- [x] Complete
- **Locations**: ConfigurePage.razor:511,533, ConnectingPage.razor:108
- **Changes**:
  - Verify `KeriaConnectionInfo.IdentifiersList` is populated by BW after connect
  - Replace `signifyClientService.GetIdentifiers()` with `appCache.MyKeriaConnectionInfo.IdentifiersList`
- **Note**: Ensure BW updates storage after `RunCreateAid`
- **Test**: Identifiers display correctly after connect and after create

### Phase 5: GetIdentifier() from Storage
- [x] Started
- [x] Complete
- **Location**: ProfilePage.razor:106
- **Changes**:
  - Fetch from `appCache.MyKeriaConnectionInfo.IdentifiersList` by prefix
- **Test**: ProfilePage displays identifier details correctly

### Phase 6: GetCredentials() from Storage
- [x] Started
- [x] Complete
- **Locations**: DashboardPage.razor:72, CredentialsPage.razor:54, WebsiteConfigDisplay.razor:257
- **Changes**:
  - Verify credentials are cached in session storage by BW
  - Replace `signifyClientService.GetCredentials()` with storage/cache access
- **Test**: Credentials display correctly on all three pages

### Phase 7: Add TODO P2 Comments for Remaining
- [x] Started
- [x] Complete
- **Locations**:
  - ProfilePage.razor:149 - `LoadKeyStateData()` (GetKeyState)
  - ProfilePage.razor:172 - `LoadKeyEventsData()` (GetKeyEvents)
  - ProfilePage.razor:401 - `SaveAlias()` (RenameAid)
- **Action**: Add `// TODO P2: Refactor to use BackgroundWorker RPC` comments

### Phase 8: Cleanup Unused Injections (Optional)
- [x] Started
- [x] Complete
- Removed `@inject ISignifyClientService` from 16 files that no longer use it:
  - WelcomePage.razor, NewReleasePage.razor, WebsitesPage.razor, WebsitePage.razor
  - KeriAgentServicePage.razor, UnlockPage.razor, GettingStartedPage.razor, DashboardPage.razor
  - AddPasskeyPage.razor, CredentialsPage.razor, ConnectingPage.razor, ConfigurePage.razor
  - Passkeys.razor, MainLayout.razor, BaseLayout.razor, WebsiteConfigDisplay.razor
- **Note**: ProfilePage.razor still injects ISignifyClientService (used by TODO P2 methods)

## Testing Checklist

After each phase, verify:
1. [ ] Build succeeds (`/build-windows`)
2. [ ] Extension loads in browser
3. [ ] Specific functionality works per phase test criteria
4. [ ] No console errors related to SignifyClientService

## Session Notes

- Started: 2026-02-03
- Last updated: 2026-02-03

# Plan: Multi-KERIA Configuration Support

## Status: COMPLETE

**All 9 phases have been implemented.** This document is now for reference only.

## Overview
Refactored the extension to support multiple KERIA Cloud Service configurations, allowing users to switch between different KERIA connections. This involved storage model changes, UI updates to UnlockPage and PreferencesPage, a new management page, and status indicator updates.

**Assumptions:**
- Clean install (no migration from existing single-config storage)
- No backward compatibility with old `KeriaConnectConfig` key

---

## Implementation Status

| Phase | Description | Status |
|-------|-------------|--------|
| 1 | New Models and Utility Classes | COMPLETE |
| 2 | Update Preferences Model | COMPLETE |
| 3 | Update WebauthnService to Use Shared Helper | COMPLETE |
| 4 | Update AppCache for New Storage Models | COMPLETE |
| 5 | Create ManageKeriAgentServicesPage | COMPLETE |
| 6 | Update UnlockPage | COMPLETE |
| 7 | Update PreferencesPage | COMPLETE |
| 8 | Update SessionStatusIndicator | COMPLETE |
| 9 | Add Menu Item | COMPLETE |

---

## Phase 1: New Models and Utility Classes - COMPLETE

**Goal:** Create all new model files and utility classes.

### Completed Items:
- [x] `Extension/Models/KeriaPreference.cs` - Stores selected KERIA connection digest and AID prefix
- [x] `Extension/Models/KeriaConnectConfigs.cs` - Storage model for multiple KERIA configurations keyed by digest
- [x] `Extension/Utilities/KeriaConnectionDigestHelper.cs` - Computes SHA256 digest from ClientAidPrefix + AgentAidPrefix + PasscodeHash
- [x] `Extension.Tests/Utilities/KeriaConnectionDigestHelperTests.cs` - 7 unit tests covering all scenarios

---

## Phase 2: Update Preferences Model - COMPLETE

**Goal:** Add new preference properties.

### Completed Items:
- [x] Added `IsMultiKeriaConfigEnabled` (default: true)
- [x] Added `IsMultiKeriaOnUnlock` (default: true)
- [x] Added `KeriaPreference` property
- [x] `SelectedPrefix` is now a computed backward-compatible property reading from `KeriaPreference.SelectedPrefix`

---

## Phase 3: Update WebauthnService to Use Shared Helper - COMPLETE

**Goal:** Refactor WebauthnService to use shared `KeriaConnectionDigestHelper`.

### Completed Items:
- [x] `WebauthnService.cs` now delegates to `KeriaConnectionDigestHelper.Compute()`

---

## Phase 4: Update AppCache for New Storage Models - COMPLETE

**Goal:** Add observers for `KeriaConnectConfigs`, update existing observers.

### Completed Items:
- [x] Added `KeriaConnectConfigs` observer
- [x] Added `MyKeriaConnectConfigs` property
- [x] Added `GetConfigByDigest()` helper
- [x] Added `GetSelectedKeriaConnectConfig()` helper
- [x] Added `GetAvailableKeriaConfigs()` helper
- [x] Updated `FetchInitialStorageValuesAsync()` to fetch KeriaConnectConfigs

---

## Phase 5: Create ManageKeriAgentServicesPage - COMPLETE

**Goal:** Add new page for managing KERIA connections.

### Completed Items:
- [x] Route added in `Routes.cs`
- [x] `ManageKeriAgentServicesPage.razor` created with:
  - List of configured KERIA connections with alias and URL
  - "Active" badge for currently selected config
  - Back navigation button
  - Placeholder for Add/Remove functionality

---

## Phase 6: Update UnlockPage - COMPLETE

**Goal:** Add KERIA Cloud Service selector to UnlockPage.

### Completed Items:
- [x] KERIA selector dropdown (visible when feature enabled and IsMultiKeriaOnUnlock is true)
- [x] `_availableKeriaConfigItems` computed from AppCache
- [x] `OnKeriaConfigDigestChanged` handler with:
  - Navigation to ManageKeriAgentServicesPage for "Add/Remove" option
  - Config change triggers app restart with snackbar notification
- [x] Uses string-based digest values for MudSelect

---

## Phase 7: Update PreferencesPage - COMPLETE

**Goal:** Add Optional Features section with multi-KERIA settings.

### Completed Items:
- [x] "Optional Features" section added
- [x] `IsMultiKeriaConfigEnabled` toggle switch
- [x] `IsMultiKeriaOnUnlock` toggle switch (conditional on IsMultiKeriaConfigEnabled)
- [x] KERIA selector dropdown (when configs exist)
- [x] Persistence handlers for both toggles

---

## Phase 8: Update SessionStatusIndicator - COMPLETE

**Goal:** Add KERIA alias to tooltip texts when multi-KERIA feature is enabled.

### Completed Items:
- [x] Added `KeriaAlias` parameter
- [x] Added `IsMultiKeriaConfigEnabled` parameter
- [x] Conditional tooltip texts showing KERIA alias:
  - Connected: "Connected to KERIA Cloud Service ({alias})"
  - Not connected: "Not connected to KERIA Cloud Service ({alias})"
  - Locked: "Locked ({alias})"

---

## Phase 9: Add Menu Item - COMPLETE

**Goal:** Add "Manage KERIA Connections" menu item.

### Completed Items:
- [x] Menu item in MainLayout navigation
- [x] Conditional visibility based on `IsMultiKeriaConfigEnabled`
- [x] Uses Cloud icon

---

## Files Summary

### New Files Created:
1. `Extension/Models/KeriaConnectConfigs.cs`
2. `Extension/Models/KeriaPreference.cs`
3. `Extension/Utilities/KeriaConnectionDigestHelper.cs`
4. `Extension/UI/Pages/ManageKeriAgentServicesPage.razor`
5. `Extension.Tests/Utilities/KeriaConnectionDigestHelperTests.cs`

### Modified Files:
1. `Extension/Models/Preferences.cs` - Added `IsMultiKeriaConfigEnabled`, `IsMultiKeriaOnUnlock`, `KeriaPreference`
2. `Extension/Routes.cs` - Added ManageKeriAgentServicesPage route
3. `Extension/Services/AppCache.cs` - Added KeriaConnectConfigs observer and helpers
4. `Extension/Services/WebauthnService.cs` - Uses shared digest helper
5. `Extension/UI/Pages/UnlockPage.razor` - Added KERIA selector
6. `Extension/UI/Pages/PreferencesPage.razor` - Added Optional Features section
7. `Extension/UI/Components/SessionStatusIndicator.razor` - Added KeriaAlias and conditional tooltips
8. `Extension/UI/Layouts/MainLayout.razor` - Added menu item, passes alias to indicator

---

## Testing Checklist

### Per-Phase Testing - All Complete:
- [x] Phase 1: Unit tests for KeriaConnectionDigestHelper pass
- [x] Phase 2: Build succeeds, existing tests pass
- [x] Phase 3: Build succeeds, WebauthnService tests pass
- [x] Phase 4: Build succeeds, AppCache compiles
- [x] Phase 5: ManageKeriAgentServicesPage loads at correct URL
- [x] Phase 6: UnlockPage shows KERIA selector (when enabled)
- [x] Phase 7: PreferencesPage Optional Features section works
- [x] Phase 8: Tooltips show alias when feature enabled
- [x] Phase 9: Menu item appears and navigates correctly

### Integration Testing - TODO:
- [ ] Select different KERIA config triggers 3-second restart
- [ ] `IsMultiKeriaConfigEnabled: false` hides all multi-KERIA UI
- [ ] `IsMultiKeriaOnUnlock: false` hides selector on UnlockPage only
- [ ] "Add/Remove" option navigates to ManageKeriAgentServicesPage

---

## Future Work

The ManageKeriAgentServicesPage currently shows a placeholder for Add/Remove functionality. Future enhancements could include:
1. Add new KERIA configuration form
2. Delete existing KERIA configurations
3. Edit existing configurations (alias, URLs)
4. Import/export configurations

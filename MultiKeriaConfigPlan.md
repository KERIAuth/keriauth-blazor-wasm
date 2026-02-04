# Multi-KERIA Config Refactoring Plan

## Overview

Refactor KERIA configuration management to support multiple configurations with improved UX, proper session handling, and dedicated pages that don't require authentication.

## Design Principles

- **Computed properties pattern:** Prefer reactive computed properties over imperative field changes where possible
- **Self-healing state:** BwReadyState is self-healing (BackgroundWorker recreates it on startup if missing)

## Design Decisions (Confirmed)

| ID | Decision | Choice |
|----|----------|--------|
| D1 | Config change on non-auth page | No redirect needed; just update preference |
| D2 | Detail page UX | Dedicated page (KeriaConfigPage.razor) |
| D3 | Code reuse | Shared components where possible |
| D4 | Passkey deletion on config delete | Offer choice, default to preserve |
| D5 | Navigation after config actions | Return to KeriaConfigsPage; "Reset All" uses ResetAllPage |
| D6 | Session cleanup on config change | Clear ALL session storage (BwReadyState self-heals) |

## Current State

**Existing pages to eventually retire:**
- `KeriAgentServicePage.razor` - single config details (auth required)
- `ManageKeriAgentServicesPage.razor` - config list with add/select
- `DeletePage.razor` - rename to ResetAllPage.razor, update text

**Recently completed:**
- KeriaConnectionInfo refactored (stores digest + IdentifiersList only)
- AppCache computed properties for session config lookup
- Passkey filtering by KeriaConnectionDigest
- MudSelect focus fix on UnlockPage

---

## Phase 1: Config Change Session Cleanup

**Goal:** When changing KERIA config, properly clear session data without forcing page reload on non-auth pages.

### Tasks

1. **Create `SessionManager.ClearSessionForConfigChange()` method**
   - Clear ALL session storage (simpler than selective clearing)
   - BwReadyState will self-heal when BackgroundWorker next initializes
   - Call `appCache.ClearKeriaConnectionInfo()` for immediate AppCache sync
   - Log the config change

2. **Update `UnlockPage.OnKeriaConfigDigestChanged()`**
   - Remove the 3-second delay and force reload
   - Call `SessionManager.ClearSessionForConfigChange()` (clears session, but no-op effect since not authenticated)
   - Update `_selectedKeriaConfigDigest` and passkey count via computed property pattern
   - Show snackbar "Configuration changed" (no restart message)

3. **Update `PreferencesPage` config change handler**
   - Call `SessionManager.ClearSessionForConfigChange()` to lock
   - Update preference with new digest
   - Stay on PreferencesPage (no navigation)
   - Show snackbar "Configuration changed. Session locked."

4. **Handle empty state in KeriaConfigsPage (moved from Phase 6)**
   - When zero configs, show appropriate message
   - Prompt to add first config or reset

5. **Unit Tests**
   - Test `SessionManager.ClearSessionForConfigChange()` clears session storage
   - Test that clearing doesn't affect local storage (KeriaConnectConfigs, Preferences)
   - Test BwReadyState is recreated after session clear (integration test)

### Manual Testing
- [ ] On UnlockPage: Change config via MudSelect, verify no reload, passkey count updates
- [ ] On PreferencesPage: Change config, verify session locks, stay on page
- [ ] Verify Dashboard redirects to Unlock after config change from PreferencesPage
- [ ] Verify BackgroundWorker recovers after session storage cleared

---

## Phase 2: KeriaConfigsPage (List View)

**Goal:** Create new unauthenticated page showing all KERIA configurations with ability to add, select, and navigate to details.

### Tasks

1. **Create `KeriaConfigsPage.razor`**
   - Route: `/KeriaConfigs.html`
   - No authentication required (similar to PreferencesPage pattern)
   - Layout: MainLayout

2. **Page Content**
   - Header: "KERIA Cloud Service Connections"
   - Count badge: "(N)"
   - List of configs with:
     - Left-edge highlight for selected config (reuse pattern from ManageKeriAgentServicesPage)
     - Cloud icon (primary color if selected)
     - Alias name
     - AdminUrl (caption)
     - Passkey count badge per config (computed property)
     - Click row → navigate to KeriaConfigPage with digest in path

3. **Actions**
   - "Add Config" button (if `IsMultiKeriaConfigEnabled` in preferences)
     - For now: Navigate to existing ConfigurePage or show "Coming soon"
   - "Reset All" button → Navigate to ResetAllPage.razor
   - Back button → previous page

4. **Update MainLayout menu**
   - Add "KERIA" menu item pointing to KeriaConfigsPage
   - Rename existing menu items with "RETIRED-" prefix:
     - "RETIRED-KERIA Cloud Service" (KeriAgentServicePage)
     - "RETIRED-Manage KERIA Configs" (ManageKeriAgentServicesPage)

5. **Rename DeletePage.razor → ResetAllPage.razor**
   - Update route
   - Update text for "Reset All" context
   - Clears all storage including passkeys
   - Force-reload to index.html on confirm

6. **Unit Tests**
   - Test passkey count computation per config (computed property)

### Manual Testing
- [ ] Navigate to KeriaConfigsPage from new menu item
- [ ] Verify all configs displayed with correct info
- [ ] Verify selected config highlighted
- [ ] Verify passkey counts per config
- [ ] Click config row → navigates to detail page (Phase 3)
- [ ] "Reset All" navigates to ResetAllPage
- [ ] ResetAllPage cancel → returns to KeriaConfigsPage
- [ ] ResetAllPage confirm → all data cleared, app reloads
- [ ] **Multi-tab test:** Open multiple App tabs, change config on one tab, verify other tabs handle lock appropriately

---

## Phase 3: KeriaConfigPage (Detail View)

**Goal:** Create dedicated page for viewing/managing a single KERIA configuration.

### Tasks

1. **Create `KeriaConfigPage.razor`**
   - Route: `/KeriaConfig.html/{digest}` (path-based like ProfilePage)
   - No authentication required
   - Layout: MainLayout

2. **Page Content**
   - Header: "KERIA Cloud Service Connection - @alias"
   - Details (computed properties where applicable):
     - Admin URL
     - Boot URL (if available)
     - Client AID Prefix
     - Agent AID Prefix
     - Passkey count for this config
   - "This is the active configuration" badge (if selected)

3. **Actions**
   - "Set as Active" button (if not already active)
     - Calls `SessionManager.ClearSessionForConfigChange()` if authenticated
     - Updates preference
     - Shows snackbar
   - "Edit Alias" button → inline edit or dialog
   - "Delete" button
     - If this is the only config: Show warning "This is your only configuration"
     - If has passkeys: Show dialog with checkbox "Also delete N passkey(s) for this configuration" (unchecked by default)
     - Confirm → delete config, optionally delete passkeys
     - Navigate back to KeriaConfigsPage

4. **Edge Cases**
   - Invalid/missing digest param → redirect to KeriaConfigsPage
   - Config not found → show error, redirect to KeriaConfigsPage

5. **Session/Preference digest mismatch handling (moved from Phase 6)**
   - Call `ValidateSessionDigestMatchesPreference()` when setting active
   - Handle mismatch gracefully (clear session, update UI)

6. **Unit Tests**
   - Test delete config preserves passkeys by default
   - Test delete config with passkey deletion option
   - Test digest mismatch detection

### Manual Testing
- [ ] Navigate to detail page from list via path `/KeriaConfig.html/{digest}`
- [ ] Verify all config details displayed
- [ ] "Set as Active" locks session if authenticated
- [ ] "Edit Alias" updates config
- [ ] "Delete" with passkeys shows checkbox option
- [ ] "Delete" preserves passkeys when unchecked
- [ ] "Delete" removes passkeys when checked
- [ ] After delete, returns to list page
- [ ] Invalid digest redirects to list page

---

## Phase 4: Add Config Flow (Shared Components)

**Goal:** Enable adding a new KERIA configuration with shared components extracted from ConfigurePage.

### Tasks

1. **Extract shared components from ConfigurePage**
   - `PasscodeEntryComponent.razor` - passcode input with validation
   - `KeriaConnectionTestComponent.razor` - test connection to KERIA
   - `RecoveryPhraseDisplayComponent.razor` - show recovery phrase

2. **Create "Add Config" flow on KeriaConfigsPage**
   - "Add Config" button → dialog or inline form
   - Required fields:
     - Alias (user-friendly name)
     - Admin URL
     - Boot URL
     - Passcode (21 chars)
   - "Test Connection" button
   - On success: Create config, compute digest, store, show success
   - Navigate to new config's detail page

3. **Update ConfigurePage to use shared components**
   - Replace inline code with component references
   - Maintain existing flow

4. **Unit Tests**
   - Test shared components in isolation
   - Test add config flow creates valid config with unique digest

### Manual Testing
- [ ] "Add Config" from KeriaConfigsPage
- [ ] Enter valid credentials, test connection
- [ ] Config created and appears in list
- [ ] New config has unique digest
- [ ] Can select new config as active

---

## Phase 5: Menu and Navigation Updates

**Goal:** Finalize navigation and retire old pages.

### Tasks

1. **Update MainLayout AppBar tooltip**
   - Include current KERIA Config alias in tooltip text

2. **Update navigation throughout app**
   - Dashboard "KERIA Cloud Service" link → KeriaConfigsPage
   - Any other references to old pages

3. **Remove RETIRED- pages**
   - Delete `KeriAgentServicePage.razor`
   - Delete `ManageKeriAgentServicesPage.razor`
   - Remove their menu entries

4. **Update UnlockPage MudSelect**
   - "Add/Remove KERIA Cloud Service" → navigates to KeriaConfigsPage

### Manual Testing
- [ ] AppBar tooltip shows current config alias
- [ ] All navigation works correctly
- [ ] No broken links throughout app
- [ ] RETIRED pages removed

---

## Phase 6: Polish and Edge Cases

**Goal:** Handle remaining edge cases and polish UX.

### Tasks

1. **Validation improvements**
   - Prevent duplicate aliases (warn but allow)
   - Validate URLs before save

2. **Passkey UX improvements**
   - On config detail page, link to passkey management
   - Show warning when selecting config with no passkeys

3. **Error handling**
   - Network errors during connection test
   - Storage quota exceeded
   - Invalid config data recovery

### Manual Testing
- [ ] All edge cases handled gracefully
- [ ] Error messages are user-friendly
- [ ] No crashes or unhandled exceptions

---

## File Changes Summary

### New Files
- `Extension/UI/Pages/KeriaConfigsPage.razor`
- `Extension/UI/Pages/KeriaConfigPage.razor`
- `Extension/UI/Components/PasscodeEntryComponent.razor` (Phase 4)
- `Extension/UI/Components/KeriaConnectionTestComponent.razor` (Phase 4)

### Renamed Files
- `Extension/UI/Pages/DeletePage.razor` → `Extension/UI/Pages/ResetAllPage.razor`

### Modified Files
- `Extension/Services/SessionManager.cs` - add `ClearSessionForConfigChange()`
- `Extension/UI/Pages/UnlockPage.razor` - remove reload on config change
- `Extension/UI/Pages/PreferencesPage.razor` - proper config change handling
- `Extension/UI/Pages/ConfigurePage.razor` - use shared components (Phase 4)
- `Extension/UI/Layouts/MainLayout.razor` - menu updates, AppBar tooltip
- `Extension/UI/Pages/DashboardPage.razor` - navigation updates

### Files to Retire (Phase 5)
- `Extension/UI/Pages/KeriAgentServicePage.razor`
- `Extension/UI/Pages/ManageKeriAgentServicesPage.razor`

---

## Notes

- Each phase should be committed separately for easy rollback
- User approval required before proceeding to next phase
- Manual testing checklist must pass before phase is complete
- Unit tests should be added as part of each phase, not deferred
- Use computed properties pattern over imperative field changes

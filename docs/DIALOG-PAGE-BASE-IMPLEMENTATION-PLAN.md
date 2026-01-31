# DialogPageBase Implementation Plan

## Overview

This document describes the plan to create a `DialogPageBase` class to consolidate shared code across dialog pages that use `DialogLayout`. The goal is to improve consistency, fix existing bugs, and reduce code duplication (DRY).

## Problem Statement

The following pages share significant duplicated code:
- `RequestSignInPage.razor`
- `RequestSignDataPage.razor`
- `RequestSignHeadersPage.razor`
- `RequestCreateCredentialPage.razor`
- `RequestUnknownPage.razor`

### Shared Patterns (Currently Duplicated)
- State: `PageRequestId`, `TabId`, `HasRepliedToPage`, `OriginStr`, `IsInitialized`, `ActiveTab`
- Injected services: `appPortService`, `pendingBwAppRequestService`, `appCache`, etc.
- Methods: `ClearPendingRequestAsync()`, cancel message sending, `DisposeAsync()` pattern
- Behavior: Cancel on popup close, waiting for AppCache, returning to prior UI

### Existing Bugs to Fix
| Bug | Affected Page | Line(s) |
|-----|---------------|---------|
| Cancel doesn't clear pending request | `RequestCreateCredentialPage` | 206-224 |
| DisposeAsync doesn't send cancel | `RequestSignHeadersPage` | 288-293 |
| ClearPendingRequestAsync fetches fresh request instead of using captured ID | `RequestSignHeadersPage` | 303-311 |
| HasRepliedToPage never set | `RequestUnknownPage`, `RequestSignHeadersPage` | Various |
| ClearPendingRequestAsync called twice | `RequestSignHeadersPage` | 330, 335 |

## Architecture

### Inheritance Hierarchy

```
LayoutComponentBase
    └── BaseLayout (UI state, session display, port events)
            └── DialogLayout (popup close, return navigation)

ComponentBase
    └── AuthenticatedPageBase (render suppression when not authenticated)
            └── DialogPageBase (NEW: shared request state + methods)
                    └── RequestSignInPage
                    └── RequestSignDataPage
                    └── RequestSignHeadersPage
                    └── RequestCreateCredentialPage
                    └── RequestUnknownPage
```

### DialogPageBase Design

#### Shared State (Protected Properties)
| Property | Type | Purpose |
|----------|------|---------|
| `PageRequestId` | `string` | Request ID from web page for reply correlation |
| `TabId` | `int` | Browser tab ID |
| `HasRepliedToPage` | `bool` | Prevents duplicate cancel on dispose |
| `OriginStr` | `string` | Origin URL of requesting page |
| `IsInitialized` | `bool` | Controls conditional rendering |
| `ActiveTab` | `BrowserTab?` | Tab info for display |
| `Layout` | `DialogLayout?` | Cascading parameter for navigation |

#### Injected Services
| Service | Used For |
|---------|----------|
| `IAppPortService` | Send messages to BackgroundWorker |
| `IPendingBwAppRequestService` | Clear pending requests |
| `AppCache` | Access pending requests, wait for cache |
| `IWebExtensionsApi` | Get tab info |
| `ILogger<DialogPageBase>` | Logging |

#### Protected Methods
| Method | Purpose |
|--------|---------|
| `SendCancelMessageAsync(reason)` | Send cancel to BW, set `HasRepliedToPage` |
| `ClearPendingRequestAsync()` | Remove request by `PageRequestId` |
| `WaitForAppCacheClearAsync(timeoutMs)` | Wait for cache to reflect cleared request |
| `CancelAndReturnAsync(reason)` | Combined: send cancel → clear → wait → return |
| `LoadActiveTabAsync()` | Fetch `ActiveTab` from `TabId` |
| `InitializeFromPendingRequestAsync<T>(expectedType)` | Validate & extract payload |

#### Virtual Methods (For Page Customization)
| Method | Purpose |
|--------|---------|
| `GetCancelReason()` | Default cancel message text (override for custom) |
| `OnRequestInitializedAsync()` | Called after base initialization succeeds |

---

## Implementation Phases

### Phase 1: Create DialogPageBase (No Migration Yet)

**Goal:** Create the base class in isolation, with unit tests, before any pages use it.

**Files:**
| Action | File |
|--------|------|
| Create | `Extension/UI/Components/DialogPageBase.cs` |
| Create | `Extension.Tests/UI/Components/DialogPageBaseTests.cs` |

**Unit Tests:**
- `SendCancelMessageAsync_SendsMessageAndSetsFlag`
- `SendCancelMessageAsync_HandlesExceptionGracefully`
- `ClearPendingRequestAsync_WithValidPageRequestId_RemovesRequest`
- `ClearPendingRequestAsync_WithEmptyPageRequestId_DoesNothing`
- `WaitForAppCacheClearAsync_ReturnsTrue_WhenCacheClears`
- `WaitForAppCacheClearAsync_ReturnsFalse_OnTimeout`
- `DisposeAsync_SendsCancelMessage_WhenNotReplied`
- `DisposeAsync_DoesNotSendCancel_WhenAlreadyReplied`
- `InitializeFromPendingRequestAsync_ReturnsPayload_WhenTypeMatches`
- `InitializeFromPendingRequestAsync_ReturnsNull_WhenTypeMismatch`

**Checkpoint 1:** Run unit tests → All pass → Ready for Phase 2

---

### Phase 2: Migrate RequestUnknownPage

**Goal:** Migrate simplest page to validate base class works in real extension.

**Files:**
| Action | File |
|--------|------|
| Modify | `Extension/UI/Pages/RequestUnknownPage.razor` |
| Create | `Extension.Tests/UI/Pages/RequestUnknownPageTests.cs` |

**Changes:**
- Inherit from `DialogPageBase`
- Remove duplicated state/services
- Use base class methods

**Unit Tests:**
- `OnInitializedAsync_SetsIsInitialized_WhenPendingRequestExists`
- `Cancel_CallsCancelAndReturnAsync`

**Manual Testing Checklist:**
- [ ] Open extension, trigger unknown request type
- [ ] Verify page displays correctly
- [ ] Click Cancel → popup closes, no console errors
- [ ] Close popup without clicking Cancel → no console errors
- [ ] Check BackgroundWorker logs for cancel message

**Checkpoint 2:** Unit tests pass → Manual test pass → Ready for Phase 3

---

### Phase 3: Migrate RequestCreateCredentialPage

**Goal:** Migrate page and fix missing `ClearPendingRequestAsync` bug in Cancel.

**Files:**
| Action | File |
|--------|------|
| Modify | `Extension/UI/Pages/RequestCreateCredentialPage.razor` |
| Create/Update | `Extension.Tests/UI/Pages/RequestCreateCredentialPageTests.cs` |

**Bug Fixed:** Cancel now calls `ClearPendingRequestAsync` (was missing)

**Unit Tests:**
- `Cancel_ClearsPendingRequest` (regression test for bug fix)
- `CreateCredentialHandler_SendsApprovalMessage`
- `CreateCredentialHandler_SetsHasRepliedToPage`

**Manual Testing Checklist:**
- [ ] Trigger create credential request from test page
- [ ] Verify page displays schema SAID and credential data
- [ ] Click Cancel → verify pending request cleared (check storage)
- [ ] Click Create Credential → verify message sent, popup closes
- [ ] Close popup without action → verify cancel sent

**Checkpoint 3:** Unit tests pass → Manual test pass → Ready for Phase 4

---

### Phase 4: Migrate RequestSignDataPage

**Goal:** Migrate standard-complexity page.

**Files:**
| Action | File |
|--------|------|
| Modify | `Extension/UI/Pages/RequestSignDataPage.razor` |
| Create/Update | `Extension.Tests/UI/Pages/RequestSignDataPageTests.cs` |

**Unit Tests:**
- `SignDataHandler_SignsDataAndSendsReply`
- `SignDataHandler_SetsHasRepliedToPage`
- `Cancel_SendsCancelMessage`
- `HandleKeyDown_Enter_TriggersSignData`

**Manual Testing Checklist:**
- [ ] Trigger sign data request from test page
- [ ] Verify data items display correctly
- [ ] Select identifier, click Sign Data → verify signed response sent
- [ ] Click Cancel → verify cancel sent, popup closes
- [ ] Press Enter with identifier selected → triggers sign

**Checkpoint 4:** Unit tests pass → Manual test pass → Ready for Phase 5

---

### Phase 5: Migrate RequestSignInPage

**Goal:** Migrate page with multiple sign-in modes.

**Files:**
| Action | File |
|--------|------|
| Modify | `Extension/UI/Pages/RequestSignInPage.razor` |
| Create/Update | `Extension.Tests/UI/Pages/RequestSignInPageTests.cs` |

**Unit Tests:**
- `SignIn_IdentifierMode_SendsAidMessage`
- `SignIn_CredentialMode_SendsCredentialMessage`
- `SignIn_PromptMode_AllowsModeSelection`
- `Cancel_SendsCancelMessage`

**Manual Testing Checklist:**
- [ ] Trigger `SELECT_AUTHORIZE_AID` → verify Identifier mode
- [ ] Trigger `SELECT_AUTHORIZE_CREDENTIAL` → verify Credential mode
- [ ] Trigger `AUTHORIZE` → verify Prompt mode with radio buttons
- [ ] Complete sign-in in each mode → verify correct message sent
- [ ] Cancel in each mode → verify cancel sent

**Checkpoint 5:** Unit tests pass → Manual test pass → Ready for Phase 6

---

### Phase 6: Migrate RequestSignHeadersPage

**Goal:** Migrate most complex page, fix multiple bugs.

**Files:**
| Action | File |
|--------|------|
| Modify | `Extension/UI/Pages/RequestSignHeadersPage.razor` |
| Create/Update | `Extension.Tests/UI/Pages/RequestSignHeadersPageTests.cs` |

**Bugs Fixed:**
1. `DisposeAsync` now sends cancel message when closed without action
2. `ClearPendingRequestAsync` uses captured `PageRequestId` (not fresh fetch)
3. Remove duplicate `ClearPendingRequestAsync` call
4. Add `HasRepliedToPage` tracking

**Unit Tests:**
- `DisposeAsync_SendsCancelMessage_WhenNotReplied` (regression for bug 1)
- `ClearPendingRequestAsync_UsesCapturedPageRequestId` (regression for bug 2)
- `ApproveRequestHandler_ClearsPendingRequestOnce` (regression for bug 3)
- `ApproveRequestHandler_SetsHasRepliedToPage` (regression for bug 4)
- `Cancel_SendsCancelMessage`

**Manual Testing Checklist:**
- [ ] Trigger sign headers request (GET method)
- [ ] Trigger sign headers request (POST method)
- [ ] Click Approve & Sign → verify headers signed, popup closes
- [ ] Click Reject → verify cancel sent
- [ ] Close popup without action → verify cancel sent (bug 1 fix)
- [ ] Toggle auto-sign checkbox → verify preference saved

**Checkpoint 6:** Unit tests pass → Manual test pass → Complete

---

## Summary

| Phase | Scope | Unit Tests | Manual Checks |
|-------|-------|------------|---------------|
| 1 | Create `DialogPageBase` | 10 | 0 |
| 2 | Migrate `RequestUnknownPage` | 2 | 5 |
| 3 | Migrate `RequestCreateCredentialPage` | 3 | 5 |
| 4 | Migrate `RequestSignDataPage` | 4 | 5 |
| 5 | Migrate `RequestSignInPage` | 4 | 5 |
| 6 | Migrate `RequestSignHeadersPage` | 5 | 6 |
| **Total** | | **28** | **26** |

---

## File Changes Summary

| Action | File |
|--------|------|
| Create | `Extension/UI/Components/DialogPageBase.cs` |
| Create | `Extension.Tests/UI/Components/DialogPageBaseTests.cs` |
| Modify | `Extension/UI/Pages/RequestUnknownPage.razor` |
| Modify | `Extension/UI/Pages/RequestCreateCredentialPage.razor` |
| Modify | `Extension/UI/Pages/RequestSignDataPage.razor` |
| Modify | `Extension/UI/Pages/RequestSignInPage.razor` |
| Modify | `Extension/UI/Pages/RequestSignHeadersPage.razor` |
| Create/Update | `Extension.Tests/UI/Pages/RequestUnknownPageTests.cs` |
| Create/Update | `Extension.Tests/UI/Pages/RequestCreateCredentialPageTests.cs` |
| Create/Update | `Extension.Tests/UI/Pages/RequestSignDataPageTests.cs` |
| Create/Update | `Extension.Tests/UI/Pages/RequestSignInPageTests.cs` |
| Create/Update | `Extension.Tests/UI/Pages/RequestSignHeadersPageTests.cs` |

---

## Related Documentation

- [PAGE-CS-MESSAGES.md](./PAGE-CS-MESSAGES.md) - Message protocol reference
- [CLAUDE.md](../CLAUDE.md) - Project coding guidelines

---

*Document created: 2026-01-31*
*Status: Planning*

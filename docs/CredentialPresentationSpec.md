# Credential Presentation Specification

This document specifies how the extension responds to credential presentation requests via the IPEX protocol. It covers the full flow from receiving an `/ipex/apply` to granting a credential presentation, including credential selection, selective disclosure, and UX components.

---

## 1. Credential Presentation Request (`/ipex/apply`)

### 1.1 Apply Message Structure

An inbound IPEX apply for presentation arrives as an exchange notification with route `/exn/ipex/apply`. Key fields:

```json
{
  "t": "exn",
  "d": "<exchange SAID>",
  "i": "<verifier AID prefix (sender)>",
  "rp": "<holder AID prefix (target)>",
  "r": "/ipex/apply",
  "a": {
    "s": "<requested schema SAID>",
    "a": { /* optional attribute filter */ }
  }
}
```

- **`i`** — the verifier's AID prefix (sender of the apply) // likely the disclosee in the case of a presentation request
- **`rp`** — the holder's AID prefix (the user, the apply target) // likely the discloser in the case of a presentation request
- **`a.s`** — the schema SAID the verifier is requesting (e.g., `ENPXp1vQzRF6JwIuS-mp2U8Uf1MoADoP_GqQ62VsDZWY` for Legal Entity vLEI)
- **`a.a`** — optional attribute name/value pairs used for credential filtering (e.g., `{"LEI": "EE111Corp"}`)
- **`d`** — the exchange SAID, used as `applySaid` for response threading

### 1.2 Entry Point

The primary entry point is the **Notifications Page**. When the user expands an `/exn/ipex/apply` notification, the `NotificationExchangeDetail` component displays action buttons. The user's intent to respond as a presentation (vs. issuance) is confirmed by which button they click:

- **"Grant Presentation"** — responds with a held credential (presentation flow)
- **"Grant Credential"** — issues a new credential (issuance flow)

Note: `ExchangeView.InferFlowFromRole` exists for programmatic flow detection but may not be reliable in all cases. The button selection is the authoritative signal.

### 1.3 Content Script Path (Future — Out of Scope)

Presentation requests initiated by web pages via the content script (`/KeriAuth/ipex/apply` with `isPresentation: true`) are deferred to a future phase.
Note that the existing "/signify/authorize/credential" request message type is perhaps more appropriately handled as a presentation request.

---

## 2. Credential Presentation (`/ipex/grant` or `/ipex/offer`)

### 2.1 Grant Path (Primary — Fully Specified)

**Flow: Apply → Grant**

The holder selects a credential, configures selective disclosure, and grants it directly.

- **Full disclosure**: Uses the existing `grantReceivedCredential` TypeScript function, wired through `SignifyClientService.GrantReceivedCredential` → `PrimeDataService.PresentStep`. This fetches the credential by SAID from KERIA, wraps `sad`/`anc`/`iss` in `Serder`, and calls `ipex.grant()` + `submitGrant()`.
- **Selective disclosure**: Replaces ACDC section objects with their SAID strings before granting. See [Section 4](#4-selective-disclosure) for details.

### 2.2 Offer Path (Future — Not Implemented)

The offer path (Apply → Offer → Agree → Grant) is out of scope. Only the direct grant path (Apply → Grant) is supported. The "Offer Presentation" button is removed from the UI. If the offer path is needed in the future, it would embed the same ACDC structure (potentially elided) as the grant, using `ipexOfferAndSubmit` instead of `ipexGrantAndSubmit`.

### 2.3 Elision Map Data Model

```csharp
// Keys: ACDC section paths ("a", "e", "r")
// Values: true = disclose fully, false = elide (compact to SAID)
Dictionary<string, bool> ElisionMap
```

Note that ACDC sections may have subsections for the "oneOf" construct.
This map originates in the UI, is serialized through port messaging, and is consumed by the TypeScript layer (signify-ts) to build the compacted ACDC.

---

## 3. Credential Selection

### 3.1 Filtering

When the user clicks "Grant Presentation" on an apply notification:

1. **Schema filter**: Use `CredentialHelper.FilterCredentials(credentials, [("sad.s", schemaSaid)])` where `schemaSaid` comes from the apply's `a.s` field.
2. **Attribute filter** (optional): If the apply specifies attribute values in `a.a`, further filter by those values (e.g., `("sad.a.LEI", "EE111Corp")`). These filters narrow the credential list but do **not** force disclosure of those attributes.

### 3.2 UI: MudDialog (Modal)

The "Grant Presentation" button opens a `MudDialog` directly (no intermediate confirmation step). The dialog contains:

1. **Credential list**: Filtered credentials displayed as `CredentialComponent` cards
2. **Selection**: Single-select — user taps a card to select it. On selection, the dialog shows the full `CredentialComponent` for the selected credential with `IsPresentation=true`. Disclosure configuration (oneOf checkboxes, Min/Max presets) is handled by the `CredentialComponent` itself (see §4, §12.7).
3. **Action buttons**: "Grant" (submit) and "Cancel"

### 3.3 Empty State

If no cached credentials match the schema SAID (and optional attribute filters), display:
- Informational message: "You don't hold any credentials matching this request"
- The requested schema name (from `SchemaService`) and SAID
- Any attribute filters from the apply

---

## 4. Selective Disclosure

### 4.1 Section-Level Elision

ACDC schemas use `oneOf` patterns allowing either the full object or its SAID string for sections:

- **Attributes (`a`)**: Full attribute object or its SAID.
- **Edges (`e`)**: Full edges object or its SAID
- **Rules (`r`)**: Full rules object or its SAID

Note that Attrubutes and perhaps Edges and Rules may contain subsections of "oneOf" that also need to be considered in the design.

The schema's `oneOf` structure (already parsed in `CredentialPanelDetail.razor`) determines which sections support elision.

### 4.2 Elision Mechanism

To elide a section:
1. Read the section's `d` field — this is the section's SAID (e.g., `"EML-cMWn9wj2ajEa8xhB1ELXqMh8_eOQj44cLxxG8bUK"` for the attributes block)
2. Replace the entire section object with that SAID string in the ACDC

Example — full ACDC:
```json
{
  "d": "EM2OaYtLsnwdAs965pgODksuItWA-gZxZI9BUqaOqhs7",
  "a": {
    "d": "EML-cMWn9wj2ajEa8xhB1ELXqMh8_eOQj44cLxxG8bUK",
    "i": "ECPWMoWNsn6y7uv9ryW-HoxRsfwWY5nJP_Mv_i6Q9ivJ",
    "LEI": "EE111Corp",
    "dt": "2026-03-27T20:28:36.591000+00:00"
  },
  "e": { "d": "EBVFO-kz4fvd8HTO3TU9j7BllunpurX541oluJgqVwrE", "qvi": { ... } },
  "r": { "d": "EGZ97EjPSINR-O-KHDN_uw4fdrTxeuRXrqT5ZHHQJujQ", ... }
}
```

Elided (attributes and rules compacted, edges disclosed):
```json
{
  "d": "EM2OaYtLsnwdAs965pgODksuItWA-gZxZI9BUqaOqhs7",
  "a": "EML-cMWn9wj2ajEa8xhB1ELXqMh8_eOQj44cLxxG8bUK",
  "e": { "d": "EBVFO-kz4fvd8HTO3TU9j7BllunpurX541oluJgqVwrE", "qvi": { ... } },
  "r": "EGZ97EjPSINR-O-KHDN_uw4fdrTxeuRXrqT5ZHHQJujQ"
}
```

### 4.3 Elision Controls via `IsPresentation`

`IsPresentation` implies elision controls — there is no separate `AllowElision` parameter.

When `IsPresentation = true` on a CredentialComponent:
- Display a checkbox on each `oneOf` block. The checkbox label is "Disclose".
  - **Unchecked (default)**: Section is elided. Display: section label + SAID string (e.g., "Attributes: EML-cMWn9...")
  - **Checked**: Section is fully disclosed. Display: full section contents.
- Disclosure presets: "Max" button checks all disclosure checkboxes; "Min" button unchecks all (see §12.6.g).

When `IsPresentation = false`:
- No checkboxes shown; sections displayed as-is (read-only view).

### 4.4 C# Elision Helper

New static method in `CredentialHelper.cs`:

```csharp
public static RecursiveDictionary ElideAcdc(
    RecursiveDictionary fullAcdc,
    Dictionary<string, bool> elisionMap)
```

For each section key in the elision map where value is `false`:
1. Get the section's dictionary via `fullAcdc.GetByPath(sectionKey)`
2. Read its `d` field (the section's SAID)
3. Replace the section in the ACDC with the SAID string via `fullAcdc.SetByPath(sectionKey, saidString)`

**Important**: After elision, the top-level digest (`d`) must be recomputed. The elided ACDC is a newly constructed, fully valid ACDC with a distinct top-level `d` value computed via a saidify function. This helper constructs the elided structure; the top-level `d` computation may happen here (if feasible in C#) or be delegated to signify-ts (see §4.5 Option C).

### 4.5 TypeScript Grant Function

For selective disclosure grants, three options:
- **Option A**: Modify `ipexGrantAndSubmit` to accept a pre-elided ACDC dict with top-level `d` already recomputed in C# (verify `new Serder(elidedAcdc)` handles mixed-type dicts with string sections)
- **Option B**: Create a new `grantWithElidedAcdc` function that accepts the elided ACDC + original `anc`/`iss`/`ancatc`, and recomputes the top-level `d` in TypeScript via `Saider.saidify`
- **Option C** (preferred): Construct the elided ACDC structure in C# (§4.4), then pass it to signify-ts for saidify (top-level `d` recomputation) and grant submission. This splits the work: C# handles the structural transformation, signify-ts handles the cryptographic digest and protocol submission. Low risk because signify-ts saidify is well-tested.

Decision deferred to implementation. Option C is preferred if the C#-to-TS handoff is clean. Reference: signify-ts and/or KERIA source code contain test scripts for saidify that may clarify the exact function signature and behavior.

### 4.6 Open Research: SAID Consistency Under Elision

An elided ACDC is a **newly constructed ACDC** with a distinct top-level `d` value. The top-level `d` must be recomputed (via saidify) after sections are replaced with their SAIDs. This means:

- The elided ACDC's top-level `d` will **differ** from the original credential's `d`
- The section SAIDs (e.g., `a.d`, `e.d`, `r.d`) remain unchanged — they are the same whether the section is expanded or compacted
- The `anc`, `iss`, and `ancatc` from the original credential reference the original `d` — their validity for an elided ACDC with a different top-level `d` needs verification

signify-ts `Saider.saidify(sad)` computes the digest. `Saider.verify(sad)` validates consistency. Test scripts in the signify-ts and/or KERIA source code may provide concrete examples of elided grant construction.

---

## 5. UX — Card View

### 5.1 Credential Selection List

Uses `CredentialComponent` (Card mode) with:
- Schema title (from `SchemaService`)
- Distinguishing attribute per credential type (LEI, role, name)
- Background color keyed by schema SAID
- Selection highlighting (`IsSelected` state)

Note: The existing distinction between `CredentialPanel`, `CredentialPanelCompact`, and `CredentialPanelDetail` is awkward, particularly on `NotificationsPage` with its lazy initialization. First priority is clean presentation on `CredentialsPage.razor` using the new `CredentialComponent`. NotificationsPage migration can be deferred.

### 5.2 Presentation Mode

When `IsPresentation = true`, each `oneOf` section in the selected credential displays:
- A `MudCheckBox` labeled "Disclose"
- Unchecked (default/elided): shows section label + SAID
- Checked (disclosed): shows full section contents

---

## 6. UX — Tree View (TBD)

Tree view details will be defined in a future iteration. Placeholder requirements:

### 6.1 Request Tree (Future)
- **Deferred**: An `/ipex/apply` is not a complete or valid credential, so it requires special presentation considerations. For now, requests are presented to the user as raw JSON. Structured request display should be addressed after all other work in this specification is completed.
- When implemented: Verifier AID, requested schema, attribute filters

### 6.2 Response Tree
- Structured display of the contemplated `/ipex/grant` credential presentation
- Per-section disclosure state (elided vs. disclosed)
- Receives `IsPresentation` parameter for interactive disclosure toggles

### 6.3 Implementation Notes
- When elided: show section label + SAID
- When disclosed: show full section contents
- Candidate component: `MudTreeView`, inspired by the following code:
```
  <MudPaper Width="300px" Elevation="0">
    <MudTreeView Items="@TreeItems" SelectionMode="SelectionMode.MultiSelection" @bind-SelectedValues="SelectedValues">
        <ItemTemplate>
            @{
                // Casting context from TreeItemData<string> to our own derived class TreeItemPresenter
                // for convenient usage in the template
                var presenter = context as TreeItemPresenter;
            }
            <MudTreeViewItem @bind-Expanded="@context.Expanded" Items="@context.Children" Value="@context.Value"
                             Icon="@context.Icon" Text="@context.Text" EndText="@presenter?.Number?.ToString()" EndTextTypo="@Typo.caption" />
        </ItemTemplate>
    </MudTreeView>
</MudPaper>


@code {
    public IReadOnlyCollection<string> SelectedValues { get; set; }

    public List<TreeItemData<string>> TreeItems { get; set; } = new();
    public Dictionary<string, int?> ValueMap { get; set; }

    public class TreeItemPresenter : TreeItemData<string>
    {
        public int? Number { get; set; }

        public TreeItemPresenter(string text, string icon, int? number = null) : base(text)
        {
            Text = text;
            Icon = icon;
            Number = number;
        }
    }

    protected override void OnInitialized()
    {
        TreeItems.Add(new TreeItemPresenter("All Mail", Icons.Material.Filled.Email));
        TreeItems.Add(new TreeItemPresenter("Trash", Icons.Material.Filled.Delete));
        TreeItems.Add(new TreeItemPresenter("Categories", Icons.Material.Filled.Label) {
            Expanded = true,
            Children = [
                    new TreeItemPresenter("Social", Icons.Material.Filled.Group, 90),
                    new TreeItemPresenter("Updates", Icons.Material.Filled.Info, 2294),
                    new TreeItemPresenter("Forums", Icons.Material.Filled.QuestionAnswer, 3566),
                    new TreeItemPresenter("Promotions", Icons.Material.Filled.LocalOffer, 733)
                ]
        });
        TreeItems.Add(new TreeItemPresenter("History", Icons.Material.Filled.Label));
        ValueMap = TreeItems.Concat(TreeItems.SelectMany(x => x.Children ?? [])).OfType<TreeItemPresenter>().ToDictionary(x => x.Value, x => x.Number);
    }
}
```

---

## 7. Data Flow

### 7.1 Presentation Grant from Notification Page

```
NotificationsPage.razor
  ├── User clicks "Grant Presentation" on /exn/ipex/apply notification
  ├── Opens GrantPresentationDialog (MudDialog)                          [NEW]
  │   ├── Filters credentials by schema SAID from apply
  │   ├── User selects credential
  │   ├── User configures disclosure (IsPresentation checkboxes)
  │   └── User clicks "Grant"
  ├── appBwPortService.SendRpcRequestAsync(
  │     RequestIpexGrantPresentation,                                    [NEW message type]
  │     { senderPrefix, recipientPrefix, applySaid, credentialSaid, elisionMap })
  │
BackgroundWorker.cs
  ├── HandleAppRequestIpexGrantPresentationRpcAsync                      [NEW handler]
  │   ├── Validate payload
  │   ├── If elisionMap is all-true (full disclosure):
  │   │   └── _primeDataService.PresentStep(sender, credSaid, recipient)
  │   │       └── _signifyClient.GrantReceivedCredential(...)
  │   │           └── grantReceivedCredential (TS) → ipex.grant() + submitGrant()
  │   └── If elisionMap has false entries (selective disclosure):
  │       ├── Fetch credential, apply ElideAcdc helper
  │       └── Call new/modified TS grant function with elided ACDC
  └── Returns IpexGrantResponsePayload { success, grantSaid, error }
```

### 7.2 Port Messaging Boundary

- The App (Blazor WASM) and BackgroundWorker are separate .NET runtimes
- Credential data (`RecursiveDictionary`) preserves field ordering across the port boundary for CESR/SAID integrity
- Elision logic can run in either runtime, but the signify-ts call MUST happen in BackgroundWorker (where the client session lives)
- Currently, BackgroundWorker fetches the credential fresh from KERIA for the grant. If RecursiveDictionary round-trip fidelity is confirmed (§11.1), the cached credential could be passed across the port boundary instead, avoiding the extra KERIA fetch. This is a known risk to defer — as long as one correct path (fresh fetch or cached) is testable at each step.

---

## 8. Implementation Order

Implementation follows a dependency order. Each step builds on the ones before it. Steps A and B can run in parallel. All other steps are sequential.

```
A (records) ──────┐
                   ├──→ C (Card component) → D (pages) → E (IsPresentation) → H (TreeView) → F (dialog) → G (E2E)
B (cache refactor) ┘
```

### Step A: Record Definitions
No runtime dependencies. Pure data types.
- `SchemaIndependentDetail` enum (§12.2)
- `CredentialFieldSpec` record (§12.3)
- `CredentialViewSpec` record (§12.4)
- `CredentialViewOptions` record + `DisplayType` enum (§12.5)
- `credentialViewSpecs.json` with entries for known schemas (§12.6)
- Service to load and query view specs by schema SAID (with OOBI-then-fallback for unknown schemas)

### Step B: CachedCredentials Refactor (§11.2)
Can run in parallel with Step A.
- Refactor from single `"rawJson"` key to per-credential keyed by SAID (`"d"` field value)
- Update `CachedCredentials` model, `AppCache`, all read/write sites
- **Automated tests immediately after**

### Step C: CredentialComponent — Card Mode (§12.7)
Depends on: A, B.
- Build `CredentialComponent` as universal wrapper (refactor-first: replaces `CredentialPanelCompact`/`CredentialPanelDetail`)
- Card sub-component using `CredentialViewSpec` for field display, `CredentialViewOptions` for detail level
- ViewOptions gear panel (DisplayType, DetailLevel, IsJsonShown)
- Refactor `CredentialsPage.razor` to use `CredentialComponent` (first adoption)

### Step D: CredentialPage + TestCredentialPage (§12.7)
Depends on: C.
- New `CredentialPage` with navigation from `CredentialsPage` (preserving CredentialSaid + options)
- `TestCredentialPage` in Developer menu for testing component behavior
- Migration of other surfaces (WebsiteConfigDisplay, NotificationsPage) can be deferred

### Step E: IsPresentation + Selective Disclosure UI (§4.3)
Depends on: C.
- Add `IsPresentation` mode to `CredentialComponent`
- oneOf disclosure checkboxes (unchecked/elided by default)
- Min/Max disclosure presets
- Elision map output via callback

### Step H: TreeView Component (§12.8)
Depends on: E.
- Iterative design with try.mudblazor.com
- `CredentialTreeComponent` as alternative to Card within `CredentialComponent`
- Field labels and values as separate columns, adjustable widths
- `IsPresentation` support: tree nodes for oneOf blocks show disclosure checkboxes

### Step F: GrantPresentationDialog (§3.2)
Depends on: H.
- MudDialog with credential list filtered by schema SAID from apply
- Single-select → shows `CredentialComponent` with `IsPresentation=true`
- Grant/Cancel actions
- Enable "Grant Presentation" button in `NotificationExchangeDetail.razor`

### Step G: BackgroundWorker + signify-ts Pipeline (§7.1)
Depends on: F.
- `RequestIpexGrantPresentation` message type in `AppBwMessages.cs`
- `HandleAppRequestIpexGrantPresentationRpcAsync` in `BackgroundWorker.cs`
- Full disclosure path: wire to existing `PrimeDataService.PresentStep`
- Selective disclosure path: `ElideAcdc` helper (§4.4), saidify in signify-ts (§4.5 Option C), grant submission
- Verify `ancAttachment` validity (§10.2)

### Future (Out of Current Scope)
- **Offer presentation path**: Apply → Offer → Agree → Grant chain (§2.2)
- **Content script presentation path**: Credential selection in `RequestApproveIpexPage.razor` (§1.3)
- **Request Tree display**: Structured `/ipex/apply` display (§6.1) — raw JSON for now

---

## 9. Obsolete

Content moved to §12.2 (`SchemaIndependentDetail` enum) and §12.4 (`CredentialViewSpec.HiddenDetails` field).

---

## 10. Open Questions / Research Items

1. ~~SAID consistency under elision~~ — **Obsolete**. Addressed in §4.4, §4.5, §4.6.

2. **`ancAttachment` validity for elided ACDCs**: When presenting an elided ACDC, are the `anc`, `iss`, and `ancatc` fields from the original credential still valid? These are issuance proofs that reference the full credential.

3. **Individual attribute-level elision**: The current design supports section-level elision only (all of `a`, `e`, or `r`). Individual attribute elision within a section would require SAID recomputation and is deferred to a future phase.
Note the prior statements may be insufficient to handle nested blocks of attributes (`a`), edges (`e`), or rules (`r`). TODO: review the sample GLEIF acdc schemas or instance files (user to provide URL) and check for examples of attribute nesting.

4. ~~Multi-credential presentation~~ — **Obsolete**. IPEX does not support multi-credential grants (other than chained credentials via edges `e`).

5. ~~Schema resolution for unknown schemas~~ — **Obsolete**. Incorporated into §12.4 (OOBI resolution first, then generic fallback).

6. ~~Reject/Spurn~~ — **Obsolete**. Deferred to a separate backlog item.

---

## 11. Implementation Notes

1. ~~RecursiveDictionary round-trip fidelity~~ — **Obsolete**. Addressed in §4.4, §4.5, §4.6, and §7.2.

2. **CachedCredentials refactor**: The current `StorageArea.Session` `CachedCredentials` stores all credentials under a single `"rawJson"` key. Refactor to a collection (array/dictionary) where each credential is keyed by its SAID (`"d"` field value). This makes individual credential lookup more convenient and avoids re-parsing the entire list. Affects `CachedCredentials` model, `AppCache`, and any code that reads/writes cached credentials. **Automated tests must be written and executed immediately after this refactor.**

3. Automated tests

4. ~~New UX componentry desired~~ — **Obsolete**. Covered by §12.7 (component containment hierarchy) and §12.8 (TreeView).

5. Manual tests (enhance existing MANUAL_TESTS.md)


---

# 12. Record Definitions and Component Architecture

## 12.1 Common Conversions

1. **Credential type labels**: Schema name mapped to a friendly, abbreviated name (e.g., "Legal Entity vLEI Credential" → "LE vLEI").
2. **Detail level**: Integer 0-9 controlling how much is shown. Cosmetic only — does not affect what is sent in a grant.
3. **Default presentation** (when not further constrained):
   a. Field order per credential ACDC structure
   b. Field labels from schema `description` property (if available) or field key name
   c. Date values formatted as `YYYY-MM-DD HH:mm UTC` (determined by schema `format: "date-time"`)

## 12.2 SchemaIndependentDetail Enum

```csharp
/// Corresponds to ACDC top-level keys that are schema-independent boilerplate.
/// Used to hide fields from display (cosmetic only).
/// Underscore prefix preserves the original key casing for readability.
public enum SchemaIndependentDetail { _v, _d, _i, _ri, _s, _e, _r }
```

Mapping: `_v` → `"v"`, `_d` → `"d"`, `_i` → `"i"`, `_ri` → `"ri"`, `_s` → `"s"`, `_e` → `"e"`, `_r` → `"r"`.

## 12.3 CredentialFieldSpec Record

Defines how a single credential field is displayed at a given detail level.

```csharp
/// Specifies display properties for one field within a credential view.
public record CredentialFieldSpec(
    string Path,             // Dot-separated path in ACDC (e.g., "a.LEI", "e.qvi.n")
    int MinDetailLevel,      // Field shown when user's detail level >= this value (0 = always shown)
    string? Label = null,    // Override label (null = use schema description or field key)
    string? Format = null    // Override format (null = use schema format or raw string)
);
// Known issue: schema collections (e.g., a list of prior names for a credential holder)
// are not yet handled by CredentialFieldSpec. Path-based access assumes scalar values.
// Collection rendering will need its own treatment when encountered.
```

## 12.4 CredentialViewSpec Record

Defines the complete view specification for a credential schema at all detail levels.

```csharp
/// View specification for a credential schema. Loaded from credentialViewSpecs.json.
public record CredentialViewSpec(
    string SchemaSaid,                       // Schema SAID this spec applies to
    string ShortName,                        // Abbreviated display name (e.g., "LE vLEI")
    List<CredentialFieldSpec> Fields,         // Field display specs, ordered by display position
    List<SchemaIndependentDetail>? HiddenDetails = null  // Top-level keys hidden by default
);
```

**Detail level behavior**: A field with `MinDetailLevel = 0` is always shown. A field with `MinDetailLevel = 5` is shown only when the user's `DetailLevel >= 5`. Level 9 shows everything.

**Fallback for unknown schemas**: If no `CredentialViewSpec` exists for a schema SAID, attempt OOBI resolution first. If that fails, generate a generic spec showing all fields at all detail levels using field key names as labels.

## 12.5 CredentialViewOptions Record

Runtime state for how a CredentialComponent renders its credential.

```csharp
/// Runtime display options for a CredentialComponent.
public record CredentialViewOptions(
    DisplayType DisplayType = DisplayType.Card,   // Card or Tree
    int DetailLevel = 5,                          // 0 (most summary) to 9 (most detailed)
    bool IsPresentation = false,                  // When true: enables elision controls and disclosure presets
    bool IsAidPrefixDisplay = true,               // When true: AIDs shown via display component; false: raw string
    bool IsJsonShown = false,                      // When true: show raw JSON expansion panel
    List<string>? PreselectedPresentationPaths = null  // Initial oneOf paths pre-selected for disclosure
);

public enum DisplayType { Card, Tree }
```

**`IsPresentation` implies elision controls**: When `true`, oneOf blocks show "Disclose" checkboxes (unchecked/elided by default), and the ViewOptions panel includes disclosure presets (Min/Max buttons). When `false`, no checkboxes or presets are shown.

**`PreselectedPresentationPaths`**: Optional list of ACDC section paths (e.g., `["a", "e"]`) that should start with "Disclose" checked. Null means all elided (minimum disclosure). Used when a caller wants to pre-configure disclosure, e.g., from an apply's attribute filter.

## 12.6 ViewSpecs JSON Configuration

Loaded from `Extension/Schemas/credentialViewSpecs.json`. Example structure:

```json
{
  "viewSpecs": [
    {
      "schemaSaid": "ENPXp1vQzRF6JwIuS-mp2U8Uf1MoADoP_GqQ62VsDZWY",
      "shortName": "LE vLEI",
      "fields": [
        { "path": "a.LEI", "minDetailLevel": 0 },
        { "path": "a.i", "minDetailLevel": 3, "label": "Issuee" },
        { "path": "a.dt", "minDetailLevel": 3 },
        { "path": "a.dt", "minDetailLevel": 5, "label": "Issued", "format": "date-time" },
        { "path": "e.qvi.n", "minDetailLevel": 5 },
        { "path": "e.qvi.s", "minDetailLevel": 7 },
        { "path": "r.usageDisclaimer.l", "minDetailLevel": 7, "label": "Usage Disclaimer" },
        { "path": "r.issuanceDisclaimer.l", "minDetailLevel": 7 }
      ],
      "hiddenDetails": ["_v", "_d", "_ri", "_s"]
    }
  ]
}
```

## 12.7 Component Containment Hierarchy

### Top-Level Containers
a. **CredentialsPage** — contains a `List<CredentialComponent>`
b. **CredentialPage** — contains a single `CredentialComponent`
c. **CredentialDialog** (e.g., `GrantPresentationDialog`) — contains a `CredentialComponent`
d. **NotificationCard** — contains a `CredentialComponent`
e. **TestCredentialPage** — available from the Developer section of the menu, for testing component behavior

### CredentialPage (New)
- A new page whose main content is a `CredentialComponent`
- Receives the same parameters as `CredentialComponent` (especially `CredentialSaid`, plus initial `CredentialViewOptions`)
- **Navigation**: From an individual `CredentialComponent` on `CredentialsPage`, the user can navigate to `CredentialPage` with the same parameters (preserving CredentialSaid, DisplayType, DetailLevel, etc.)

### CredentialComponent (Universal Wrapper)
- **Parameters**: `CredentialSaid`, initial `ViewOptions` (DisplayType, DetailLevel, IsJsonShown, IsPresentation)
- **Contains either**:
  1. `CredentialCardComponent` (which may include summary and detail sub-components)
  2. `CredentialTreeComponent`
- **ViewOptions panel**: Collapsed = gear icon; expanded = full options including DisplayType, DetailLevel, IsPresentation toggles
- **When `IsPresentation = true`**: Shows disclosure preset section with "Max" and "Min" buttons (§4.3), and a callback for returning the presentation result (elision map + credential SAID)

### Refactor-First Strategy
CredentialComponent is built as the **universal replacement** for existing `CredentialPanelCompact` and `CredentialPanelDetail` components. All existing credential display surfaces (CredentialsPage, WebsiteConfigDisplay, NotificationsPage) will be migrated to use CredentialComponent.

## 12.8 TreeView Component (UX TBD)

Detailed UX for the tree view will be determined through iterative design work with the user-developer using try.mudblazor.com and the §6.3 example code with fake data. Initial requirements:

a. Field labels and values shown as **separate columns** in each tree row
b. Column widths should be **adjustable**
c. Candidate MudBlazor component: `MudTreeView` with custom `TreeItemData<string>` (see §6.3 for example code)
d. When `IsPresentation = true`: tree nodes for oneOf blocks show disclosure checkboxes







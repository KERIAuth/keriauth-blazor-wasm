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

### 2.2 Offer Path (Sketched — Future)

**Flow: Apply → Offer → Agree → Grant**

The offer path lets the holder preview what they will disclose before committing. The verifier sees the proposed disclosure and responds with an agree, after which the holder sends the final grant.

**Data model differences from grant:**
- The offer message embeds the same ACDC structure (potentially elided) as the grant would
- The offer uses `ipexOfferAndSubmit` instead of `ipexGrantAndSubmit`
- After the verifier's agree, the holder issues the grant referencing the agree SAID

Full UI and handler design for the offer presentation path is deferred to a future spec revision.

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

1. **Credential list**: Filtered credentials displayed as `CredentialPanelCompact` cards
2. **Selection**: Single-select — user taps a card to select it
3. **Disclosure configuration**: Per-section elision controls (see [Section 4](#4-selective-disclosure))
4. **Action buttons**: "Grant" (submit) and "Cancel"

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

### 4.5 TypeScript Grant Function

For selective disclosure grants, either:
- **Option A**: Modify `ipexGrantAndSubmit` to accept a pre-elided ACDC dict (verify `new Serder(elidedAcdc)` handles mixed-type dicts with string sections)
- **Option B**: Create a new `grantWithElidedAcdc` function that accepts the elided ACDC + original `anc`/`iss`/`ancatc`

Decision deferred to implementation; both options are viable.

### 4.6 Open Research: SAID Consistency Under Elision

When sections are replaced with their SAIDs, the top-level ACDC SAID (`d` field) may or may not remain valid depending on how the ACDC spec defines SAID computation:

- **If SAID is computed over the compact form**: Elision preserves the root SAID. This is the expected ACDC behavior.
- **If SAID is computed over the expanded form**: Elision changes the structure and invalidates the root SAID.

signify-ts `Saider.verify(sad)` can be used to validate. This must be verified before implementing Phase 2.

---

## 5. UX — Card View

### 5.1 Credential Selection List

Reuses `CredentialPanelCompact` with:
- Schema title (from `SchemaService`)
- Distinguishing attribute per credential type (LEI, role, name)
- Background color keyed by schema SAID
- Selection highlighting (`IsSelected` state)

### 5.2 Presentation Mode

When `IsPresentation = true`, each `oneOf` section in the selected credential displays:
- A `MudCheckBox` labeled "Disclose"
- Unchecked (default/elided): shows section label + SAID
- Checked (disclosed): shows full section contents

---

## 6. UX — Tree View (TBD)

Tree view details will be defined in a future iteration. Placeholder requirements:

### 6.1 Request Tree
- Structured display of the `/ipex/apply` content
- Verifier AID, requested schema, attribute filters

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
- The credential SAID (not the full credential) is passed across the port boundary; BackgroundWorker fetches the credential fresh from KERIA for the grant

---

## 8. Implementation Dependencies

Implementation follows a layered dependency order. Each layer builds on the ones below it. Phasing will be determined separately based on these dependencies.

### Layer 1: ACDC in JSON
- Well-understood structure (§4.2 examples). No new work needed, but serves as the reference for all layers above.

### Layer 2: ACDC in RecursiveDictionary
- Existing implementation. Round-trip fidelity (KERIA → signify-ts → C# → signify-ts → KERIA) is a parallel-track concern (§11.1). Tests needed but not a blocker for view layers.

### Layer 3: View Definition Records
- `CredentialViewSpec` and related records (§12.5). Loaded from JSON config file (`Extension/Schemas/viewSpecs.json`). Pure data — no UI, no signify-ts.

### Layer 4: Schema-Dependent View Definitions
- Per-schema SAID view specs with detail levels 0-9 (§12.5.b). Built on Layer 3 records. Includes fallback for unknown schemas (OOBI resolution, then generic display).

### Layer 5: Automated Tests
- Round-trip tests for Layer 2, unit tests for Layers 3-4, component tests for Layer 7.

### Layer 6: Credential Caching Re-evaluation
- Re-evaluate `AppCache.Credentials` adequacy for presentation flows. Fresh fetch from KERIA for grant submission; cached data acceptable for selection/preview UI.

### Layer 7: Components and Parameters
- `CredentialComponent` as universal wrapper (refactor-first: replaces existing `CredentialPanelCompact`/`CredentialPanelDetail`). Contains Card or Tree view. Parameters include `IsPresentation`, `DetailLevel`, `DisplayType` (§12.6).

### Layer 8: Pages and Dialogs
- `GrantPresentationDialog`, `CredentialsPage`, `CredentialPage`, `TestCredentialPage` (§12.6.a-d, §12.6.j). Uses Layer 7 components.

### Layer 9: End-to-End Flows
- Notification → Grant Presentation pipeline (§7). Port messaging, BackgroundWorker handler, signify-ts grant call.

### Future (Out of Current Scope)
- **Offer presentation path**: Apply → Offer → Agree → Grant chain (§2.2)
- **Content script presentation path**: Credential selection in `RequestApproveIpexPage.razor` (§1.3)

---

## 9. TBD — Future: HideSchemaIndependentDetails

Card View and Tree View will receive an optional parameter:

```csharp
List<SchemaIndependentDetail>? HideSchemaIndependentDetails
```

Where `SchemaIndependentDetail` is an enum:

```csharp
public enum SchemaIndependentDetail { V, D, I, Ri, S, E, R }
```

Corresponding to ACDC top-level keys: `"v"`, `"d"`, `"i"`, `"ri"`, `"s"`, `"e"`, `"r"`.

When a key is in the list, that field is hidden from the display. This is **cosmetic only** — it does not affect what gets sent in the grant. The purpose is to declutter the credential view by hiding boilerplate fields that are not meaningful to end users.

---

## 10. Open Questions / Research Items

1. **SAID consistency under elision**: Does the top-level ACDC `d` remain valid when sections are compacted to their SAIDs? The ACDC spec implies yes (SAID computed over compact form), but this must be verified against signify-ts `Saider.saidify` behavior, which appears to compute over the expanded form.

2. **`ancAttachment` validity for elided ACDCs**: When presenting an elided ACDC, are the `anc`, `iss`, and `ancatc` fields from the original credential still valid? These are issuance proofs that reference the full credential.

3. **Individual attribute-level elision**: The current design supports section-level elision only (all of `a`, `e`, or `r`). Individual attribute elision within a section would require SAID recomputation and is deferred to a future phase.
Note the prior statements may be insufficient to handle nested blocks of attributes (`a`), edges (`e`), or rules (`r`). TODO: review the sample GLEIF acdc schemas or instance files (user to provide URL) and check for examples of attribute nesting.

4. **Multi-credential presentation**: Some verifier requests may need multiple credentials. The current flow supports single-credential selection. Does IPEX support multi-credential grants in a single exchange?

5. **Schema resolution for unknown schemas**: If the apply requests a schema SAID not in `schemas.json`, the extension should attempt OOBI resolution first; if that fails, fall back to generic all-fields display using field key names as labels.

6. **Reject/Spurn**: The "Reject" button is a disabled stub. IPEX does not define a formal rejection message. Should the extension support an explicit rejection signal, or is non-response sufficient?

---

## 11. Unsorted User Experience Requirements and Design Idea Discussions

1. Need to test or enhance the serialization/deserialization of an ACDC with RecursiveDictionary or other structure so that it is lossless to-from typescript and json from/to signify-ts module.  RecursiveDictionary structure might not be sufficiently hardenened in a way that guarantees the calculation of a SAID will still be accurate after the path of KERIA->signify-ts->C#->signify-ts->KERIA.

2. Automated tests

3. Manual tests (enhance existing MANUAL_TESTS.md)

4. New UX componentry desired


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
public enum SchemaIndependentDetail { V, D, I, Ri, S, E, R }
```

Mapping: `V` → `"v"`, `D` → `"d"`, `I` → `"i"`, `Ri` → `"ri"`, `S` → `"s"`, `E` → `"e"`, `R` → `"r"`.

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
```

## 12.4 CredentialViewSpec Record

Defines the complete view specification for a credential schema at all detail levels.

```csharp
/// View specification for a credential schema. Loaded from viewSpecs.json.
public record CredentialViewSpec(
    string SchemaSaid,                       // Schema SAID this spec applies to
    string ShortName,                        // Abbreviated display name (e.g., "LE vLEI")
    List<CredentialFieldSpec> Fields,         // Field display specs, ordered by display position
    List<SchemaIndependentDetail>? HiddenDetails = null  // Top-level keys hidden by default
);
```

**Detail level behavior**: A field with `MinDetailLevel = 0` is always shown. A field with `MinDetailLevel = 5` is shown only when the user's `DetailLevel >= 5`. Level 9 shows everything.

**Fallback for unknown schemas**: If no `CredentialViewSpec` exists for a schema SAID, attempt OOBI resolution first. If that fails, generate a generic spec showing all fields at all detail levels using field key names as labels.

## 12.5 ViewOptions Record

Runtime state for how a CredentialComponent renders its credential.

```csharp
/// Runtime display options for a CredentialComponent.
public record ViewOptions(
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

Loaded from `Extension/Schemas/viewSpecs.json`. Example structure:

```json
{
  "viewSpecs": [
    {
      "schemaSaid": "ENPXp1vQzRF6JwIuS-mp2U8Uf1MoADoP_GqQ62VsDZWY",
      "shortName": "LE vLEI",
      "fields": [
        { "path": "a.LEI", "minDetailLevel": 0, "label": "LEI" },
        { "path": "a.i", "minDetailLevel": 3, "label": "Issuee" },
        { "path": "a.dt", "minDetailLevel": 3, "label": "Issued", "format": "date-time" },
        { "path": "e.qvi.n", "minDetailLevel": 5, "label": "QVI Credential" },
        { "path": "e.qvi.s", "minDetailLevel": 7, "label": "QVI Schema" },
        { "path": "r.usageDisclaimer.l", "minDetailLevel": 7, "label": "Usage Disclaimer" },
        { "path": "r.issuanceDisclaimer.l", "minDetailLevel": 7, "label": "Issuance Disclaimer" }
      ],
      "hiddenDetails": ["V", "D", "Ri", "S"]
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

Detailed UX for the tree view will be determined through interactive discussion with the user-developer. Initial requirements:

a. Field labels and values shown as **separate columns** in each tree row
b. Column widths should be **adjustable**
c. Candidate MudBlazor component: `MudTreeView` with custom `TreeItemData<string>` (see §6.3 for example code)
d. When `IsPresentation = true`: tree nodes for oneOf blocks show disclosure checkboxes







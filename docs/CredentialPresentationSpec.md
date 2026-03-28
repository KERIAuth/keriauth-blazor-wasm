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

### 4.3 `AllowElision` Parameter

Card View and Tree View components receive a `bool AllowElision` parameter:

- **When `true`**: Display a checkbox on each `oneOf` block. The checkbox label is "Disclose".
  - **Unchecked (default)**: Section is elided. Display: section label + SAID string (e.g., "Attributes: EML-cMWn9...")
  - **Checked**: Section is fully disclosed. Display: full section contents.
- **When `false`**: No checkboxes shown; sections displayed as-is (read-only view).

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

### 5.2 AllowElision Mode

When `AllowElision = true`, each `oneOf` section in the selected credential displays:
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
- Receives `AllowElision` parameter for interactive disclosure toggles

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
  │   ├── User configures disclosure (AllowElision checkboxes)
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

## 8. Implementation Phases

### Phase 1: Full Disclosure Grant from Notification Page
1. Enable "Grant Presentation" button in `NotificationExchangeDetail.razor`
2. Create `GrantPresentationDialog.razor` (MudDialog) with credential selection
3. Add `GrantPresentationFromApply` handler in `NotificationsPage.razor`
4. Add `RequestIpexGrantPresentation` message type in `AppBwMessages.cs`
5. Add `HandleAppRequestIpexGrantPresentationRpcAsync` in `BackgroundWorker.cs`
6. Wire to existing `PrimeDataService.PresentStep` / `GrantReceivedCredential`

### Phase 2: Selective Disclosure
1. Add `ElideAcdc` helper to `CredentialHelper.cs`
2. Create or modify TS function for granting with elided ACDC
3. Add `AllowElision` parameter to Card/Tree view components
4. Add disclosure toggle checkboxes (per `oneOf` section) to `GrantPresentationDialog`
5. Pass elision map through port messaging to BackgroundWorker
6. Verify SAID consistency under elision using `Saider.verify`

### Phase 3: Offer Presentation Path (Future)
- Enable "Offer Presentation" button
- Create offer-specific dialog and handler
- Wire offer → agree → grant chain for presentation

### Phase 4: Content Script Presentation Path (Future — Out of Scope)
- Add credential selection to `RequestApproveIpexPage.razor` for presentation applies
- Wire through BackgroundWorker with credential SAID + elision map

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

5. **Schema resolution for unknown schemas**: If the apply requests a schema SAID not in `schemas.json`, the extension can still filter by raw SAID but cannot display schema metadata. Should OOBI resolution be attempted automatically?

6. **Reject/Spurn**: The "Reject" button is a disabled stub. IPEX does not define a formal rejection message. Should the extension support an explicit rejection signal, or is non-response sufficient?

---

## 11. Unsorted User Experience Requirements and Design Idea Discussions

1. Need to test or enhance the serialization/deserialization of an ACDC with RecursiveDictionary or other structure so that it is lossless to-from typescript and json from/to signify-ts module.  RecursiveDictionary structure might not be sufficiently hardenened in a way that guarantees the calculation of a SAID will still be accurate after the path of KERIA->signify-ts->C#->signify-ts->KERIA.

2. Automated tests

3. Manual tests (enhance existing MANUAL_TESTS.md)

4. New UX componentry desired


---

# 12. Common conversions

1. credential type per schema name vs a friendly, abreviated name

2. Level of presented detail options

3. default presentation if not further specified or constrained:
   a. order per credential ACDC
   b. attribute (field) labels per schema (if available) or field name (key)
   c. data types determined by schema, especially important for Date presented in UI, which should YYYY-MM-DD HH:mm UTC

4. pre-configured CredViewSpec definitions
   a. predefined selected common fields (e.g. schema title)
   b. attributes
   c. edges
   d. rules

5. pre-configured CredViewSpec definitions for known credential schemas:
  a. (per schema SAID)
  b. for a defined detail level (0 to 9) where 0 is most summary and 9 is most detailed.  A "CredentialViewSpec" is a record that will contain these definitions.  "0" means those fields specified are always shown.  "5" means those fields are shown when the user's desired detail is 5 or greater.
  c. A CredenialViewSpec may provide an alternate label than the default based on the schema's field description.
  b. The CredentialViewSpec shall define what is displayed in the components for CredentialCard (summary or expanded mode) and CredentialTree.

6. Page and Component Containment
  1. Top-level containers:  
      a. CredentialsPage contains a List<CredentialComponent>
      b. CredentialPage contains a CredentialComponent
      c. CredentialDialog contains a CredentialComponent
      d. A NotificationCard contains a CredentialComponent
      e. CredentialComponent can contain either a:
          1. CredentialCardComponent, which may include a CredentialCardSummaryComponent and CredentialCardDetailComponent.
          2. CredentialTreeComponent
      f. CredentialComponent will contain a ViewOptions section that when collapsed, just appears as a "Gear" icon.  When expanded, it includes options, including `enum DisplayType {Card, Tree}`, `int DetailLevel` (0 to 9), `bool IsPresentation`, `bool IsAidPrefixDisplay` (indicating whether AIDs are shown as strings or using that display component), `List<string>? PreselectedPresentationPaths` (indicating the initial presentation "oneOf" blocks to be selected).
      g. When IsPresentation is true and ViewOptions are shown, a section is show that includes: Label "Disclosure preset", Button "Max" and Button "Min".  When Min is pressed, its all the "oneOf" minimal oneOf blocks (e.g. SAID) are auto-selected for presentation; and when Max is pressed, the detailed oneOf blocks are auto-selected for presentation.
      h. CredentialComponent has parameters, including the CredentialSaid, and for the initial view options, including DisplayType, DisplayLevel, and IsJsonShown.
      i. When CredentialComponent parameters include IsPresentation=true, a callback provide a mechanism for the presentation results to be handled.
      j. A TestCredentialPage will be available from the Developers secion of the menu.

7. TreeView Component
  a. This view sill need a lot of interactive UX specification discussion with the user-developer for clarity.
  b. The field values and labels should be shown as separate columns in each tree-row.  The column widths should be adjustable.







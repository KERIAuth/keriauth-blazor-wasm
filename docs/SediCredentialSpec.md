# SEDI Test Credential — Feature Spec

Status: **Proposed** — awaiting approval to begin Phase 1.

## Goal

Issue a test **State-Endorsed Digital Identity (SEDI) credential** from the existing "Issue Credentials" section of [CreateTestDataPage.razor](../Extension/UI/Pages/CreateTestDataPage.razor), via the extended [OfferOrGrantCredentialIssuanceDialog.razor](../Extension/UI/Components/OfferOrGrantCredentialIssuanceDialog.razor). Grant-only flow (no offer path).

## Decisions

- **Issuer:** constrained to local AIDs.
- **Registry:** lazy-created per issuer AID, keyed off the issuer prefix, persisted into prefs as `sediStatusRegistry`.
- **Naming:** no "Utah" references anywhere in field names, schema text, or rule disclaimers; jurisdiction-neutral language.
- **Default residence address:** `"123 S Main St, Lake City, XQ 96233"` (intentionally invalid state abbreviation and invalid ZIP format, since this is test data).
- **Coexistence:** the existing SEDI schema entry (SAID `EKEIy4dKkg1ygomPyDNJH4AiI3khx4ADy2s3hWBbsj2_`, file `verifiable-credential-schema-for-data-attestation.json`) remains untouched; this feature adds a **new** SEDI schema under a distinct filename and SAID.
- **Schema hosting:** file lives only in [Extension/Schemas/](../Extension/Schemas/) in this repo. OOBI URL is a raw GitHub URL on `main`, similar to the pattern used by the existing ECR schema.
- **Storage:** the restructure of `IssueCredentialsTestPrefs` is a breaking change to the on-disk shape; bumps `KeriaConnectConfigs.SchemaVersion`, so existing TestPrefs data is discarded (user sees `MigrationNotice`).
- **Two-level graduated disclosure** (outer `a`, per-attribute `{d,u,val}`) — enables compacting the whole attributes block to a SAID, OR disclosing the block structure but compacting individual attributes. The original triple-nested (`a` / `a.a` / per-attribute) design was flattened to `a` / per-attribute because (1) nothing in this codebase references the inner `a.a` compact form, and (2) the flat layout matches standard ACDC convention and works with stock `Saider.saidify` in a single call (no multi-level walker needed).
- **Grant only:** SEDI is not selectable from the "Issue & Offer" entry point.

## Proposed schema

### Structure

```
credential
├ v, d, u              envelope header
├ i                    issuer AID
├ ri                   registry SAID
├ s                    schema SAID
├ a (oneOf SAID|obj)   attributes block
│  ├ d, u              block SAID + nonce
│  ├ i                 issuee AID (the credential subject)
│  ├ dt                issuance datetime
│  ├ fullLegalName          oneOf[SAID | {d, u, val: string}]
│  ├ birthDate              oneOf[SAID | {d, u, val: date-time}]
│  ├ residenceAddress       oneOf[SAID | {d, u, val: string}]
│  ├ lawfulPresenceVerified oneOf[SAID | {d, u, val: boolean}]
│  ├ proofingMethod         oneOf[SAID | {d, u, val: enum}]
│  ├ proofingLevel          oneOf[SAID | {d, u, val: enum}]
│  └ portrait               oneOf[SAID | {d, u, val: base64url JPEG}]   (optional)
└ r (oneOf SAID|obj)   rules block
   ├ d
   ├ usageDisclaimer   {d, l: const string}
   └ privacyDisclaimer {d, l: const string}
```

The schema has a **single top-level `$id`** (no nested `$id` fields), so `Saider.saidify` saidifies the whole schema in one stock call. Per-attribute graduated disclosure is still supported via `oneOf[SAID, object]` on each attribute — the `$id` nesting was orthogonal to graduated disclosure and served no functional purpose.

### Departures from the original draft (for internal consistency)

1. Standard ACDC semantics: top-level `i` = issuer AID, `a.i` = issuee AID (credential subject).
2. Per-attribute graduated disclosure via `oneOf[SAID, object]` at each attribute — preserves selective disclosure of individual attributes during credential presentation.
3. Wrapped `r` in `oneOf[SAID, object]` pattern for consistency with `a`.
4. Added `d` to `usageDisclaimer` and `privacyDisclaimer` so their const text is SAID-sealed.
5. All object shapes use `additionalProperties: false` and `required`.
6. `portrait` is optional (not in `required`).
7. Renamed `utahResidenceAddress` → `residenceAddress`.
8. Removed all references to Utah and SB0275.
9. Top-level `required`: `["v", "d", "u", "i", "ri", "s", "a", "r"]` (using `ri` per vLEI convention, not `rd`).
10. Proofing enums retained as drafted.
11. Single top-level `$id` only — no nested `$id` fields. Stock `Saider.saidify` handles the whole schema in one call.

### Rule block text (jurisdiction-neutral)

- **usageDisclaimer.l** (const):
  > "Usage of a valid, unexpired, and non-revoked State-Endorsed Digital Identity Credential does not assert the holder is trustworthy, honest, or compliant with any laws beyond what is expressly verified herein."
- **privacyDisclaimer.l** (const):
  > "It is the sole responsibility of Holders to present this credential in a privacy-preserving manner using the mechanisms provided in the IPEX protocol and ACDC specification. Holders must disclose only the minimum attributes necessary for the purpose of each transaction."

## Credential instance values

| Path | Source | Shown in UI? |
|---|---|---|
| `v` | `ACDC10JSON{size}_` (computed) | no |
| `d` | saidify | no |
| `u` | signify random nonce | no |
| `i` | = `issuerPrefix` (local AID) | no (derived from Issuer field) |
| `ri` | `sediStatusRegistry` (lazy-created, persisted) | no |
| `s` | SEDI schema SAID (fixed: `EHiLGNXjNR31E8hQR1Vs9OSWrG_CSpOOkVW76ZvUkaxq`) | no |
| `a.d` / `a.u` | saidify / nonce | no |
| `a.i` | = `issueePrefix` | **Issuee** (local AIDs + connections) |
| `a.dt` | `DateTime.UtcNow` ISO-8601 at issuance | no |
| `a.fullLegalName.{d,u}` | saidify / nonce | no |
| `a.fullLegalName.val` | coin-flipped default name | **Full legal name** (text) |
| `a.birthDate.{d,u}` | saidify / nonce | no |
| `a.birthDate.val` | `DateTime.UtcNow` ISO-8601 | **Birth date** (accepts `MM/DD/YYYY`, `YYYY-MM-DD`, or ISO-8601 → normalized) |
| `a.residenceAddress.{d,u,val}` | saidify, nonce, default `"123 S Main St, Lake City, XQ 96233"` | **Residence address** |
| `a.lawfulPresenceVerified.{d,u,val}` | saidify, nonce, default `true` | **Lawful presence verified** (checkbox) |
| `a.proofingMethod.{d,u,val}` | saidify, nonce, default `"in-person-document"` | **Proofing method** (select from enum) |
| `a.proofingLevel.{d,u,val}` | saidify, nonce, default `"IAL3"` | **Proofing level** (select `IAL1`/`IAL2`/`IAL3`) |
| `a.portrait.{d,u,val}` | saidify, nonce, base64url constant `SediCredentialHelper.TestPortraitBase64Url` | no |
| `r.d` | saidify | no |
| `r.usageDisclaimer.{d,l}` | saidify, const text | no |
| `r.privacyDisclaimer.{d,l}` | saidify, const text | no |

### Default-name generation

- 10 male first names, 10 male middle names (distinct from firsts), 10 female first names, 10 female middle names (distinct from firsts), 10 last names.
- English-pronounceable, ethnically varied.
- Coin-flip at dialog open selects gender; samples `firstName`, `middleName`, `lastName` and returns `"{first} {middle} {last}"`.
- Hardcoded in C# (no external data file).

## Storage model change (BREAKING)

Per [CLAUDE.md](../CLAUDE.md) §Semi-Invariants rule #4 ("structural move"), this is a schema-breaking change to `KeriaConnectConfigs` — old test prefs are discarded, users see `MigrationNotice`.

```csharp
public record IssueEcrTestPrefs {
    public string? IssuerPrefix { get; init; }
    public string? HolderPrefix { get; init; }
    public string RoleName { get; init; } = "";
}

public record IssueSediTestPrefs {
    public string? IssuerPrefix { get; init; }
    public string? IssueePrefix { get; init; }
    public string SediStatusRegistry { get; init; } = "";
}

public record IssueCredentialsTestPrefs {
    public IssueEcrTestPrefs? Ecr { get; init; }
    public IssueSediTestPrefs? Sedi { get; init; }
}
```

---

## Phase 1 — Harden the schema

Blocks all downstream work. Once the schema is saidified and committed to `main`, the SAID is permanent.

**Steps:**

1. **Draft the final schema JSON** with all 10 departures above, jurisdiction-neutral text, and the nested `a.a` shape.
2. **Save to** `Extension/Schemas/oobi/<SAID>/index.json` (the SAID-addressed layout). The single copy is the source of truth. The embedded-resource `LogicalName` maps it back to the friendly name for schema-body lookup.
3. **Saidify the schema** using [scripts/saidify_schema.py](../scripts/saidify_schema.py) — a small KERIpy-based script that calls stock `coring.Saider.saidify`. With the flattened structure, a single call suffices (no depth walker needed). Run: `/tmp/keri-venv/bin/python3 scripts/saidify_schema.py --file <path> --output <path>`, then minify with a short Python one-liner using `json.dump(..., separators=(',',':'))`. Current SAID: `EHiLGNXjNR31E8hQR1Vs9OSWrG_CSpOOkVW76ZvUkaxq`.
4. **Add a new entry to** [Extension/Schemas/schemas.json](../Extension/Schemas/schemas.json) (the existing `EKEIy4dKkg1ygomPyDNJH4AiI3khx4ADy2s3hWBbsj2_` entry is left untouched). Done — entry uses the `raw.githubusercontent.com/.../oobi/<SAID>/index.json` URL and includes a `$comment` field marking the secondary-URL TODO.
5. **Commit the file + schemas.json entry to `main`** — this is required so the raw GitHub URL resolves. Until merged, the OOBI does not work in any non-local environment.
6. **Register the SAID in code** — add `SediSchemaSaid` to [CredentialHelper.cs](../Extension/Helper/CredentialHelper.cs) `SchemaSaids` and expose via a new `SediCredentialHelper.cs` alongside [VleiCredentialHelper.cs](../Extension/Helper/VleiCredentialHelper.cs).
7. **Resolve the schema OOBI at startup** — add `SediSchemaSaid` to the schema-OOBI resolution list in [PrimeDataService.cs:129-137](../Extension/Services/PrimeDataService/PrimeDataService.cs#L129-L137).
8. **Add credential view spec** — wire a friendly-label view spec entry into `CredentialViewSpecService` so the credential renders with human-readable field names.

**Verification:**
- Schema file parses as valid JSON Schema draft 2020-12.
- Saidify roundtrips deterministically (run twice → same SAID).
- OOBI resolves locally with signify-ts after commit to `main`.
- `schemas.json` manifest still validates against its consumer.

**Gate:** user approves the drafted schema JSON **before** saidification locks the SAID.

---

## Phase 2 — Models, storage bump, name generator

**Scope:**
- Restructure `IssueCredentialsTestPrefs` per above; add `IssueEcrTestPrefs`, `IssueSediTestPrefs`.
- Bump `KeriaConnectConfigs.SchemaVersion`; update [StorageModelRegistry.cs](../Extension/Services/Storage/StorageModelRegistry.cs); update `InitializeStorageDefaultsAsync` probe + stale-remove case; add friendly text to `MigrationNoticeBanner.razor`.
- Add `DefaultSediName.Generate()` static helper with 50-name pool.
- Add `SediConstants`: portrait base64 constant, proofing-method and proofing-level enum value lists, usage/privacy disclaimer strings, registry name `"sedi"`.

**Tests:**
- Name generator: pool sanity (no duplicates across first/middle collisions within a gender), deterministic by seeded `Random`.
- Prefs roundtrip through storage with both `Ecr` and `Sedi` subrecords populated.

**Gate:** user reviews name pool and migration text; confirms before Phase 3.

---

## Phase 3 — BW-side SEDI issuance

**Scope:**
- Add `IssueSediCredentialRequestPayload` with fields: `IssuerPrefix`, `IssueePrefix`, `FullLegalName`, `BirthDateIso`, `ResidenceAddress`, `LawfulPresenceVerified`, `ProofingMethod`, `ProofingLevel`.
- Add `IssueSediCredentialResponsePayload` mirroring [`IssueEcrCredentialResponsePayload`](../Extension/Models/Messages/AppBw/AppBwMessages.cs) (`Success`, `Acdc`, `Anc`, `Iss`, `CredentialSaid`, `Error`).
- Add `AppBwMessageType.Values.RequestIssueSediCredential` + BW handler.
- **BW handler does the work directly** (not PrimeDataService) — mirrors the existing ECR pattern at [BackgroundWorker.HandleAppRequestIssueEcrCredentialRpcAsync](../Extension/BackgroundWorker.cs). PrimeDataService is reserved for orchestrated multi-step workflows (the vLEI chain "Go" flow). Per-attribute `{d, u, val}` sub-blocks are pre-saidified by the BW (signify-ts's `credentials().issue(...)` only saidifies block-level `a.d`/`r.d`/top-level, not nested sub-blocks). Rules block sub-disclaimer `d` fields are also pre-saidified.
- **Registry handling**: uses stock `CreateRegistryIfNotExists(senderName, "sedi")` which is already idempotent at the KERIA level. The `IssueSediTestPrefs.SediStatusRegistry` prefs field is reserved but not yet populated — a `// TODO P2` is left in the handler to opt-in to prefs-cached lookup as a future optimization.
- Grant path reuses existing `RequestSubmitIpexGrant`.

**Tests:**
- Deferred to Phase 5: full SAID roundtrip test via the existing [DeveloperTestPage](../Extension/UI/Pages/DeveloperTestPage.razor) saidify verification on an actually-issued SEDI credential. This end-to-end check is more valuable than a shallow unit test on `RecursiveDictionary` ordering (which is already covered by [RecursiveDictionaryTests](../Extension.Tests/Helper/RecursiveDictionaryTests.cs)).

**Gate:** user manual-tests BW issuance end-to-end (e.g. via a temporary developer page button) before Phase 4.

---

## Phase 4 — UI: Type radio + SEDI branch in dialog

**Scope in [OfferOrGrantCredentialIssuanceDialog.razor](../Extension/UI/Components/OfferOrGrantCredentialIssuanceDialog.razor):**

- Add `Type` radio (ECR / SEDI) at top of `RenderAdHocFields` (Step 1, `DialogMode.AdHoc` only — the review-apply/review-agree paths never hit SEDI).
- When `Type = SEDI`:
  - Hide Role field.
  - Show: Issuer (local AIDs only), Issuee (local + connections), Full legal name, Birth date, Residence address, Lawful presence verified (checkbox), Proofing method (select), Proofing level (select).
  - Date field accepts `MM/DD/YYYY`, `YYYY-MM-DD`, or ISO-8601; parser normalizes to ISO-8601 on blur. Validation rule: `"Birth date must be a valid date"`.
  - Validation rules (mirror existing `AdHocValidationRules`): issuer & issuee selected, not equal, full legal name non-empty, birth date valid, residence address non-empty.
  - Defaults from `IssueSediTestPrefs` if present; otherwise coin-flipped name, `UtcNow` birth date, fallback to `appCache.Aids[0].Prefix` for issuer.
- On "Issue & Grant" confirm with SEDI type: route to `RequestIssueSediCredential`, then reuse the existing grant submit path.
- SEDI is **not** available when dialog is invoked with `IsOffer=true`. Gate the radio option accordingly (or rely on the entry-point restriction — TBD during implementation).
- Save `IssueSediTestPrefs` on successful grant (parallel to current ECR save path in [CreateTestDataPage.razor:483-488](../Extension/UI/Pages/CreateTestDataPage.razor#L483-L488)).

**Gate:** user manual browser test — issue SEDI, grant to a holder, verify the credential renders with friendly labels via the Phase 1 view spec.

---

## Phase 5 — Polish

**Scope:**
- Validation-message refinement.
- Date-parsing edge cases (leap years, invalid months, ambiguous formats).
- Graduated-disclosure SAID-ordering roundtrip check (issue → serialize → re-saidify → verify SAIDs match).
- Any UX refinements surfaced during Phase 4 testing.

---

## Out of scope

- Polaris-web / web-page integration for SEDI credential presentations.
- Production-grade identity proofing data or real portrait capture.
- Revocation workflow (existing stub snackbar applies).
- Retiring or modifying the pre-existing SEDI schema entry at SAID `EKEIy4dKkg1ygomPyDNJH4AiI3khx4ADy2s3hWBbsj2_`.

---

## Open items tracked in this session

- **Schema OOBI server URL (secondary)** — user will provide a dedicated SEDI schema-OOBI server URL later. A `// TODO P1` comment will be placed in [schemas.json](../Extension/Schemas/schemas.json) next to the SEDI entry during Phase 1. Tracked as a session TODO, not a GitHub issue.

Below is a **backlog-ready specification** written in **Markdown**, designed to be:

* **Actionable for implementation**
* **Stable enough to reference in future AI prompts**
* **Explicit about scope, constraints, and open questions**

You can paste this directly into your product backlog, repo, or docs.

---

# Schema-Driven Credential Rendering Specification

> **Purpose**
> Define a schema-driven, template-based system for rendering credentials (ACDC instances) into UI components (e.g., MudBlazor “Credential Cards”) without requiring per-credential code.

---

## Example AI Prompt (Reference)

> **Prompt:**
> You are implementing a schema-driven credential rendering system in a Blazor + MudBlazor application.
>
> Inputs:
>
> * A JSON Schema describing a credential (ACDC schema)
> * A JSON Credential Layout Template conforming to the specification below
> * An instance credential payload (ACDC)
>
> Your task is to:
>
> 1. Validate the layout template against the Template Schema
> 2. Resolve schema pointers and instance data pointers
> 3. Render the credential into a MudBlazor-based card UI using semantic control mappings
> 4. Respect user preferences (density, expansion, redaction)
>
> Follow the “Schema-Driven Credential Rendering Specification” exactly.
> Do not invent new control types or template fields.

---

## 1. Overview

This specification defines a **data-driven rendering system** where:

* **Credential Schema** defines *what data exists*
* **Credential Layout Template** defines *how data is presented*
* **Credential Instance (ACDC)** provides the actual values
* **Renderer** maps semantic layout controls to UI primitives (e.g., MudBlazor)

The system must support:

* Multiple credential schemas
* User-defined layouts
* Safe, deterministic rendering
* Strong validation boundaries

---

## 2. Inputs

### 2.1 Credential Schema

* Format: JSON (JSON Schema–like)
* Identified by a stable identifier (e.g., SAID, hash, URI)
* Defines:

  * Properties and nesting
  * Types and formats
  * Optional validation rules

### 2.2 Credential Instance (ACDC)

* Format: JSON
* Must conform to the referenced Credential Schema
* Treated as **untrusted display input**, even if cryptographically verified

### 2.3 Credential Layout Template

* Format: **JSON (canonical runtime format)**
* May be authored or imported from CSV or other tools
* Validated against a **Template Schema**
* Declares:

  * Layout structure
  * Data bindings
  * Display semantics
  * Formatting and conditional rules

---

## 3. Credential Layout Template Model

### 3.1 Template Metadata

Each template MUST include:

```json
{
  "templateId": "string",
  "version": "string",
  "appliesTo": {
    "schemaId": "string",
    "issuerConstraints": []
  }
}
```

* `schemaId`: Identifies the credential schema this template applies to
* `issuerConstraints` (optional): issuer-specific applicability rules

---

### 3.2 Node Types (Layout Tree)

Templates are **tree-structured**. Supported node types:

| Node Type | Purpose                 |
| --------- | ----------------------- |
| `card`    | Root container          |
| `section` | Group of related fields |
| `field`   | Single data binding     |
| `repeat`  | Iterate over array data |
| `divider` | Visual separation       |

---

### 3.3 Field Node Specification

```json
{
  "type": "field",
  "label": "Role",
  "schemaPtr": "#/properties/role",
  "dataPtr": "/role",
  "display": {
    "control": "text",
    "variant": "body",
    "emphasis": "primary"
  },
  "transform": [
    { "op": "truncate", "args": [64] }
  ],
  "when": {
    "exists": "/role"
  }
}
```

#### Field Attributes

| Attribute         | Description                               |
| ----------------- | ----------------------------------------- |
| `schemaPtr`       | JSON Pointer into the credential schema   |
| `dataPtr`         | JSON Pointer into the credential instance |
| `display.control` | Semantic control type                     |
| `display.variant` | Typography or visual hierarchy            |
| `transform`       | Declarative formatting pipeline           |
| `when`            | Conditional display rules                 |

---

## 4. Semantic Control Types

The template uses **semantic controls**, not UI framework components.

### 4.1 Core Controls

| Control  | Semantics                           |
| -------- | ----------------------------------- |
| `text`   | Inline or block text                |
| `badge`  | Status / short label                |
| `chip`   | Categorical value                   |
| `kv`     | Key–value row                       |
| `list`   | Vertical list                       |
| `table`  | Tabular data                        |
| `link`   | Clickable URI                       |
| `status` | Valid / invalid / warning indicator |

### 4.2 Layout Controls

| Control   | Semantics        |
| --------- | ---------------- |
| `section` | Grouped content  |
| `divider` | Visual separator |
| `stack`   | Vertical layout  |
| `grid`    | Column layout    |

> **Note:** The renderer maps these to MudBlazor primitives (e.g., `MudText`, `MudChip`, `MudStack`).

---

## 5. Transform Pipeline

Transforms are **pure, declarative, ordered operations**.

### 5.1 Allowed Operations (initial set)

| Operation   | Purpose              |
| ----------- | -------------------- |
| `coalesce`  | First non-null value |
| `truncate`  | Limit string length  |
| `uppercase` | Text transform       |
| `date`      | Date formatting      |
| `datetime`  | Date/time formatting |
| `mask`      | Partial redaction    |
| `mapEnum`   | Enum → label         |

Transforms MUST:

* Be deterministic
* Have bounded runtime
* Not execute arbitrary code

---

## 6. Conditional Rendering (`when`)

Supported conditions (initial set):

| Condition  | Description         |
| ---------- | ------------------- |
| `exists`   | Data pointer exists |
| `equals`   | Exact match         |
| `matches`  | Regex match         |
| `gt`, `lt` | Numeric comparison  |

Conditions are **read-only** and cannot mutate state.

---

## 7. Rendering Rules

* All templates MUST be validated before rendering
* All data values MUST be HTML-escaped by default
* Maximum limits MUST be enforced:

  * Max nesting depth
  * Max repeat count
  * Max string length
* Missing data MUST NOT crash rendering
* Renderer MUST fail closed (empty / placeholder) rather than throwing

---

## 8. Blazor / MudBlazor Integration (Non-Normative)

* Rendering implemented via:

  * `RenderFragment` composition
  * `DynamicComponent` for control dispatch
* User preferences (density, expansion, redaction) are inputs to renderer
* Templates MUST NOT reference MudBlazor component names or CSS classes directly

---

## 9. CSV as an Authoring Format (Optional)

* CSV MAY be supported as an **authoring/import format**
* CSV MUST be compiled into canonical JSON templates
* CSV is NOT a runtime format

Suggested CSV columns:

```
schemaPtr, dataPtr, label, control, variant, emphasis, transform, when
```

---

## 10. Non-Goals

This specification explicitly does NOT attempt to:

* Execute user-supplied code
* Support runtime Razor compilation
* Act as a general UI programming language
* Replace full custom credential renderers where needed

---

## 11. Open Design & Specification Questions

These questions SHOULD be resolved before implementation:

1. **Schema Evolution**

   * How are templates versioned when schemas change?
   * Can templates declare compatibility ranges?

2. **Localization**

   * Are `label` values literal strings or localization keys?
   * How are locale-specific formats handled?

3. **Actions & Interactivity**

   * Should templates support actions (copy, open, verify)?
   * If so, how are actions declared safely?

4. **Redaction & Privacy**

   * Are redaction rules user-driven, issuer-driven, or both?
   * Should templates declare *privacy sensitivity levels*?

5. **Extensibility**

   * How are new control types introduced without breaking older templates?
   * Is a capability negotiation mechanism needed?

6. **Theming**

   * How do semantic `emphasis` values map to theme palettes?
   * Are issuer-specific themes supported?

7. **Security Boundaries**

   * Are templates trusted artifacts, user-supplied, or both?
   * Do different trust levels impose different limits?

---

## 12. Success Criteria

The system is successful if:

* New credential schemas can be rendered without new UI code
* Templates are reviewable, auditable, and versioned
* Rendering is safe, deterministic, and visually consistent
* Power users can customize layouts without breaking the UI

---

**End of Specification**

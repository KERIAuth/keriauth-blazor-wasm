# ViewDef Feature Requirements

## Goals

Provide a consistent, user-facing mechanism for filtering and viewing collections across the extension's pages. Users should be able to switch between different perspectives on their data — such as viewing items relevant to their active profile versus seeing everything — with a single control that appears uniformly across all collection pages.

## Problem Statement

Collection pages currently handle filtering inconsistently:
- Some pages implicitly filter by the active profile (Credentials, Connections) with no way for the user to see unfiltered data.
- Other pages show everything with no profile-scoped view (Websites, Notifications).
- Passkeys are shown per-KERIA-connection but there is no way to see all passkeys across connections.
- There is no standard UI pattern for switching between these perspectives.

## Expected User Experience

### View Selector

Each collection page displays a **View selector** below the page heading. This consists of:
- A **dropdown** labeled "View" (only visible when multiple views are defined for that page).
- A **count label** to the right, showing the number of items currently displayed in a format appropriate to the active view.

When only a single view is defined for a page, the dropdown is hidden and only the count label is shown.

### Count Display

The count label format adapts to the active view's filtering context:
- **No filtering** (e.g., "All profiles" view): "(3)" — just the total count.
- **Profile-filtered** (e.g., "Active profile" view): "(3 for profile)" — the count of items matching the active profile.
- **Role-filtered with subtotal** (e.g., "Held (active profile)" on Credentials): "(3 of 10 in profile)" — the role-filtered count out of all items related to the active profile.
- **Filtered with total context** (e.g., future use): "(3 of 10)" — filtered count out of total.

### View Persistence

The user's selected view on each page is remembered across page navigations and browser sessions. When returning to a page, the previously selected view is automatically restored. If a saved view no longer exists (e.g., after an update), the page's default view is used.

### Per-Page Views

#### Credentials (4 views)
- **Held (active profile)**: Credentials where the active profile is the holder/issuee. Count: "(N of M in profile)".
- **Issued (active profile)**: Credentials where the active profile is the issuer. Count: "(N of M in profile)".
- **Held or issued (active profile)** *(default)*: All credentials involving the active profile in any role. Count: "(N for profile)".
- **All profiles**: All credentials regardless of profile. Count: "(N)".

#### Connections (2 views)
- **Active profile** *(default)*: Connections initiated from the active profile. Count: "(N for profile)".
- **All profiles**: All connections. Count: "(N)".

#### Websites (2 views)
- **Active profile** *(default)*: Websites configured for the active profile. Count: "(N for profile)".
- **All profiles**: All websites. Count: "(N)".

#### Notifications (3 views)
- **Recipient (active profile)**: Notifications where the active profile is the recipient. Count: "(N of M in profile)".
- **Issuer (active profile)**: Notifications where the active profile is the issuer/sender. Count: "(N of M in profile)".
- **All** *(default)*: All notifications. Count: "(N)".

Existing notification filters (by route type and read/unread status) remain available and compose with the view selection.

#### Passkeys (2 views)
- **Current KERIA connection** *(default)*: Passkeys associated with the currently active KERIA configuration. Count: "(N for profile)".
- **All**: All stored passkeys across all KERIA connections. Count: "(N)".

#### Profiles (1 view)
- **All profiles**: All profiles, sorted alphabetically by name. Count: "(N)".

#### KERIA Configs (1 view)
- **All configs**: All KERIA configurations, sorted alphabetically by alias. Count: "(N)".

#### Presentations
Deferred until the Presentations page is implemented.

## Design Principles

1. **LINQ-friendly**: View definitions use `Func<T, bool>` predicates that compose naturally with LINQ's `Where()`, `OrderBy()`, and related operators.
2. **Reactive**: Views re-evaluate automatically when the active profile or underlying data changes, following the app's existing reactive subscription pattern.
3. **Consistent**: All collection pages share the same `ViewSelector` component and `ViewDef` type system, providing a uniform user experience.
4. **Extensible**: The structure supports future additions — new views per page, user-configurable views, additional sort orders, and alternate layout modes (e.g., grid vs. cards) — without structural changes.
5. **Non-invasive**: For pages with a single view, the dropdown is hidden. The feature adds capability without cluttering pages that don't need it.

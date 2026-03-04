# Branding

This directory contains trademark and brand identity documentation for the DIGN Identity Wallet.

## Contents

| File | Purpose |
|------|---------|
| [trademark-policy.md](trademark-policy.md) | Trademark usage policy for the "DIGN" name and logos |

## Brand Assets in Other Locations

For build simplicity, branded assets remain in their original locations within the Extension project:

- **Logos**: `Extension/wwwroot/images/` (sized PNGs)
- **Terms of Use**: `Extension/wwwroot/content/terms.html`
- **Privacy Policy**: `Extension/wwwroot/content/privacy.html`

## Runtime Source of Truth
The primary brand constants are defined in:
- **C#**: `AppConfig.ProductName` in `Extension/AppConfig.cs`
- **TypeScript**: `PRODUCT_NAME` in `scripts/types/src/brand.ts` (exported from `@keriauth/types`)
- **Manifest**: `Extension/wwwroot/manifest.json` (`name`, `short_name`, `default_title`)

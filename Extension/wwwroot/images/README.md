# Logo Images

`logo*` icons are the full-color variant used for extension identity, favicons, PWA, and the active toolbar state.
`logob*` icons are the monochrome/badge variant used for the default (inactive) toolbar state.

## Usage Matrix

| File | manifest.json `action` | manifest.json `icons` | webmanifest | HTML favicons | HTML apple-touch / splash | app.ts active | app.ts inactive |
|------|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| `logo016.png` | | x | | x | | x | |
| `logo024.png` | | | | | | x | |
| `logo032.png` | | x | | x | | x | |
| `logo048.png` | | x | x | | | x | |
| `logo096.png` | | | | x | | | |
| `logo128.png` | | x | | | | x | |
| `logo180.png` | | | | | x | | |
| `logo192.png` | | | x | | | | |
| `logo512.png` | | x | x | | | | |
| `logob016.png` | x | | | | | | x |
| `logob024.png` | x | | | | | | x |
| `logob032.png` | x | | | | | | x |
| `logob048.png` | x | | | | | | x |
| `logob128.png` | x | | | | | | x |

Razor files also reference some of these logos.

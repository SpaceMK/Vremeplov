# Binance Theme (Unity)

A native Unity port of the **Binance** design language — *bold Binance Yellow on a
monochrome, trading-floor surface stack* — inspired by the `DESIGN.md` entry in
[VoltAgent/awesome-claude-design](https://github.com/VoltAgent/awesome-claude-design).

That collection targets **Claude Design** (a web workspace that emits CSS/HTML).
This project is a **Unity AR app** (uGUI + TextMeshPro), so the web kit can't drop in
directly — this folder recreates the same visual language as runtime Unity theming.

> Brand note: these tokens are inspired by publicly observable patterns. Binance is a
> third-party trademark; using its exact look in a shipped product is your call and your
> responsibility re: brand/trademark policy.

## Design tokens

### Color

| Token            | Hex       | Use                                   |
|------------------|-----------|---------------------------------------|
| Yellow           | `#FCD535` | Primary accent / CTA fill             |
| Yellow (dark)    | `#F0B90B` | Pressed / hover                       |
| Background       | `#0B0E11` | App canvas (deepest)                  |
| Surface          | `#181A20` | Panels / sheets                       |
| Surface (raised) | `#1E2329` | Cards / inputs                        |
| Line             | `#2B3139` | Borders / dividers / secondary button |
| Text primary     | `#EAECEF` | Headings & body                       |
| Text secondary   | `#848E9C` | Muted / captions                      |
| Text disabled    | `#5E6673` | Disabled                              |
| On-yellow        | `#0B0E11` | Text/icon on a yellow fill            |
| Positive         | `#0ECB81` | Buy / up                              |
| Negative         | `#F6465D` | Sell / down                           |

Defined once in `Scripts/Runtime/BinancePalette.cs` and mirrored on the editable
`BinanceTheme` asset.

### Type

- Brand font is **BinancePlex**, which is derived from **IBM Plex Sans** — so IBM Plex
  Sans (bundled here under OFL in `Fonts/`) is a near-exact, freely licensed match.
- Weights shipped: Regular, Medium, SemiBold, Bold.
- Body text → Regular; headings & button labels → SemiBold.

### Feel

Dense, flat, high-contrast "trading-floor" layout: dark surfaces, thin `#2B3139`
dividers, a single loud yellow CTA per view, tight spacing.

## Setup (3 steps, in the Unity Editor)

Run these from the **`Tools ▸ Binance Theme`** menu, in order:

1. **Generate Fonts** — bakes the IBM Plex Sans TTFs into TMP SDF font assets
   (`Fonts/IBMPlexSans-*.SDF.asset`). Uses a *dynamic* atlas (glyphs render on demand —
   light and mobile-friendly).
2. **Create or Update Theme Asset** — creates `BinanceTheme.asset` and wires the fonts.
3. **Apply To Open Scenes** — adds a `BinanceThemeController` to every root Canvas in the
   open scene(s) and re-skins it. **Save the scene** to keep the changes.

> The `ARScene` template UI is themed automatically by step 3.

### Login screen

`Assets/Scenes/LoginScene.unity` already contains the themed **"Welcome Back!"** login —
authored directly in the scene (no menu/build step). Layout follows the travel-app reference
(glowing orb, email/password fields, "Remember me" + "Forgot Password?", a yellow **Sign in**
CTA, **Google** sign-in, "Sign up" footer) — Apple sign-in intentionally omitted.

- Real **TMP** components throughout: `TextMeshProUGUI` for all text, `TMP_InputField` for
  the email + password fields (password masked), `Button` for the CTAs.
- Colors are the Binance design tokens above, applied directly to each element.
- Font is currently the bundled TMP default (**LiberationSans SDF**) so it renders out of the
  box. To switch to IBM Plex per the type guide, run step 1 (Generate Fonts) and set the
  generated SDF asset on the text components (or drop a `BinanceThemeController` on
  `LoginCanvas`).
- **UI only** — no click handlers yet. A few icons ("Show", back chevron, top-right dot) are
  text/sprite placeholders to swap for real icons.

## How it works

- **`BinanceThemeController`** (on a Canvas root) re-skins everything beneath it in one
  pass — at runtime via `Awake`, or from the editor menu / its inspector "Apply" button.
  Set `tintCameraBackground` only on non-AR scenes (AR needs the live camera feed visible).
- Roles are **inferred** by default: buttons → secondary, bold/large text → heading,
  other text → body, images → surface.
- For precise control, add a **`ThemedElement`** to an object and pick a `UIRole`
  (e.g. mark the main CTA `PrimaryButton` to get the yellow fill).

## Files

```
BinanceTheme/
├─ Fonts/                     IBM Plex Sans TTFs + OFL license (+ generated SDF assets)
├─ Scripts/Runtime/
│  ├─ BinancePalette.cs       static color tokens (fallback source of truth)
│  ├─ UIRole.cs               semantic roles
│  ├─ BinanceTheme.cs         editable ScriptableObject (palette + fonts)
│  ├─ ThemeApplier.cs         stateless "apply theme to one element" logic
│  ├─ ThemedElement.cs        per-element role tag
│  └─ BinanceThemeController.cs  canvas-level one-pass re-skinner
└─ Scripts/Editor/
   ├─ BinanceThemeMenu.cs            the 3 setup menu items
   └─ BinanceThemeControllerEditor.cs  inspector "Apply" button
```

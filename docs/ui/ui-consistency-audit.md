# Tuvima Library UI Consistency Audit

Date: 2026-04-26

## Scope

This audit reviewed `src/MediaEngine.Web` for visual inconsistencies beyond color: input sizing, form layout, labels/help text, button variants, cards/panels, spacing, radius, shadows, MudBlazor defaults, inline styles, and duplicate page-specific CSS patterns.

## Summary

The web UI has a usable shared design direction, but `app.css` still contains several layered visual systems:

- early cinematic/glass app variables and overrides
- Vault-standard settings primitives such as `st-page`, `st-card`, and `st-toggle-row`
- legacy provider/listen/card alignment overrides
- Hybrid Paces + Metronic admin variables and overrides
- a later Tuvima token-backed cascade

The newest Tuvima token system should become the foundation. Existing screen classes should migrate toward `tl-*` primitives while remaining aliases keep current pages working.

## Inconsistent Form And Input Styling

High-impact areas:

- `Components/Settings/*`: many settings tabs mix `MudTextField`, `MudSelect`, `MudNumericField`, `MudSwitch`, `MudPaper`, and inline `Style="@...Style"` strings. Examples include `ServerGeneralTab.razor`, `WikidataConfigTab.razor`, `UniverseSettingsTab.razor`, `SecurityTab.razor`, `SetupTab.razor`, and `UsersTab.razor`.
- `Components/Collections/CollectionEditorShell.razor`: uses the strongest shared dialog pattern today (`AppDialogShell`, `app-dialog-field`, `app-dialog-select`, `app-artwork-picker`) but still has local rule-row and editor-specific sizing.
- `Components/MediaEditor/SharedMediaEditorShell.razor`: contains many Mud fields and buttons with `sme-*` classes and dense custom CSS. This is a good candidate to alias onto `tl-form`, `tl-field`, and `tl-action-bar` in phases.
- `Components/LibraryItems/InspectorSearchSection.razor` and `InspectorCoverPicker.razor`: use raw `<input>` and `<select>` controls with local classes rather than shared `tl-input`, `tl-select`, or `tl-filter-bar`.
- `Components/Watch/TvBrowsePage.razor`: uses raw `<input>` and `<select>` in the browse toolbar instead of Mud controls or shared form classes.

## Inline Styles

Inline styles are concentrated in:

- settings tabs using `Style="@InnerCardStyle"`, `Style="@SectionCardStyle"`, and `Style="@ToggleRowStyle"`
- library item inspectors and detail drawers with inline icon sizes, colors, range controls, and helper text
- `ReportProblemDialog.razor`, which contains inline flex/text styles and should use shared dialog/body/action classes
- provider tester and enrichment tester tabs using inline `MudPaper` panel styling
- collection and media editor dialogs where some inline styles remain for dynamic behavior

Recommended migration: keep dynamic styles for actual runtime values, but replace static layout/spacing/color/radius/shadow inline styles with `tl-*` classes.

## Duplicate Card And Panel Patterns

Duplicate card/panel systems found:

- `st-card`, `st-card--accent`
- `provider-card`, `provider-card-slotted`, `provider-card-unavailable`
- `chain-card`
- `drawer-panel`
- page-local panels such as `settings-system-card`, `user-overview-panel`, `settings-overview-card`
- one-off `MudPaper Elevation="0" Class="pa-* mb-*" Style="@...Style"` settings panels
- media editor `sme-*` panels and state cards

New foundation:

- `tl-card`
- `tl-card--raised`
- `tl-card--selected`
- `tl-card--warning`
- `tl-panel`
- `tl-section`

Backward-compatible aliases have been added so existing `st-*`, provider, chain, and drawer panel classes inherit token-driven styling while pages are renamed gradually.

## MudBlazor Defaults Used Inconsistently

Common inconsistencies:

- `MudTextField`, `MudSelect`, `MudAutocomplete`, and `MudNumericField` are sometimes dense, sometimes default, and sometimes locally restyled.
- `MudButton` variants are used directly without consistent size or semantic class mapping.
- `MudPaper` is frequently used as a page panel with inline padding and local backgrounds.
- `MudDialog`, `MudMenu`, and `MudPopover` styling varies by dialog type.
- `MudSwitch` and `MudCheckBox` are mostly functional but not consistently wrapped in shared setting rows.

Global overrides now normalize Mud control height, radius, borders, focus rings, disabled state, dialog/menu/popover surfaces, button sizing, and switch/checkbox accent behavior.

## Hard-Coded Sizes

Patterns found:

- control heights: `36px`, `40px`, `42px`, `48px`, `58px`
- card/dialog radius: `8px`, `12px`, `14px`, `16px`, `18px`, `20px`, `24px`, `30px`
- panel padding: `12px`, `16px`, `20px`, `24px`, `32px`
- shadows: many page-specific `0 8px`, `0 16px`, `0 24px`, `0 28px`, `0 32px` shadows

New control tokens:

- `--tl-control-height-sm: 36px`
- `--tl-control-height-md: 42px`
- `--tl-control-height-lg: 48px`
- `--tl-control-radius: 10px`
- `--tl-control-padding-x: 12px`
- `--tl-control-gap: 12px`
- `--tl-form-row-gap: 16px`
- `--tl-form-section-gap: 24px`
- `--tl-page-gap: 24px`
- `--tl-card-padding: 20px`

## Page-Specific CSS To Promote

Promote these patterns into shared classes over time:

- library search/filter toolbars -> `tl-filter-bar`
- settings cards and rows -> `tl-card`, `tl-setting-row`, `tl-form-section`
- provider cards and chain cards -> `tl-card` variants
- collection editor fields -> `tl-field`, `tl-control-sm`, `tl-action-bar`
- media editor inline buttons and field grids -> `tl-button-*`, `tl-form-grid`
- review/manual verification forms -> `tl-form`, `tl-form-row`, `tl-field-error`
- dialog action footers -> `tl-action-bar`

## Recommended Priority Order

1. Keep the new `tl-*` foundation and aliases in `app.css` as the migration layer.
2. Rename settings pages from `st-*` classes to `tl-*` classes one tab at a time.
3. Replace settings `Style="@...Style"` panel constants with shared card/section classes.
4. Convert provider configuration and tester pages from inline `MudPaper` styling to `tl-card`, `tl-form`, and `tl-action-bar`.
5. Normalize library and review filter/search toolbars with `tl-filter-bar` and `tl-control-sm`.
6. Convert raw `<input>` and `<select>` controls in library/watch/inspector screens to `tl-input` and `tl-select`, or wrap Mud controls in `tl-control-sm/md/lg`.
7. Migrate media editor `sme-*` field and button classes onto shared `tl-*` primitives while preserving its layout.
8. Collapse duplicate root variable blocks in `app.css` after all pages no longer depend on the older variable names.

## Current Implementation Started

- Added centralized control, form, spacing, card, page, z-index, and transition tokens to `tuvima.tokens.css`.
- Added shared `tl-form`, `tl-form-section`, `tl-form-grid`, `tl-form-row`, `tl-field`, label/help/error classes, control rows, toolbar/filter/action bars, inputs/selects/textareas/search controls, button sizes/variants, cards, panels, and MudBlazor control normalization.
- Added backward-compatible aliases for `st-page`, `st-page-header`, `st-page-title`, `st-page-subtitle`, `st-card`, `st-card--accent`, `st-toggle-row`, `provider-card`, `chain-card`, `drawer-panel`, `app-dialog-field`, and `app-dialog-select`.
- Migrated the Activity settings page away from sample data and static inline timeline styles. It now renders real `/activity/*` ledger data with reusable `activity-*` classes.
- Migrated the Libraries configured-library cards, organization preview panel, and Report Problem dialog away from static inline styles into reusable `tl-card--compact`, `tl-panel`, `tl-section-*`, `tl-chip-muted`, and dialog-local classes.
- Browse URL parsing and grouping metadata have been extracted from `MediaBrowseShell.razor` into `BrowseQueryBuilder` and `BrowseState`; keep moving pure query/card/hero logic out before changing browse visuals.
- Left behavior, routes, bindings, and validation unchanged.

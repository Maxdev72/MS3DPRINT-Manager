# Client data and printability design

## Scope

Improve the existing portable Windows WPF manager without changing the storage root or duplicating classified files. Keep individual clients supported, but do not show professional-only company lookup/SIRET controls or a duplicate primary-contact identity for them.

## Client lookup

Professional clients may search a French company by name or SIRET. Suggestions use the free public Recherche d'entreprises API, appear after a short debounce, and never block manual entry when offline. Selecting a suggestion fills company name, SIRET and available address; all remain editable. A separate address completion uses the current IGN Geoplateforme completion API. The personal-client form does not query company data. Persist optional SIRET in a backward-compatible profile field.

## Refresh

Mutations refresh the visible catalog/dashboard after successful completion. A debounced filesystem watcher refreshes the visible view when Nextcloud changes the storage tree. No polling timer. Watcher errors leave manual refresh available.

## Calendar and classification

Calendar popups need explicit legible light/dark colors, including inactive days and selected-day states. Classification must continue to move exactly one source file without overwrite. Replace the generic access-denied message in this workflow with a diagnostic that identifies the failed path/operation while preserving the source on failure. Do not add a copy-delete fallback without verifying why File.Move failed.

## 3D wall analysis

The C#/.NET GPU viewer gets an opt-in, cancellable, off-UI-thread geometry analysis with user-adjustable minimum thickness in millimeters. It flags possible thin zones and reports mesh validity/limitations. Results are advisory, not a slicer simulation or a printability certification; units must be confirmed because STL does not encode units. No source model is changed.

## Verification

Use red/green regression tests for services and behavior, UI smoke/markup tests for WPF controls, full Release tests, and a portable publish check before deployment. Preserve all existing user files and settings.

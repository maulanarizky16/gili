# Fork Migration Status

This document tracks the adaptation of Gili for an English-language, international audience.

## Scope

### Remove completely
- Prayer calculations and schedules.
- Prayer, adhan and imsak reminders, notifications, sounds and tray controls.
- Hijri calendar calculations and presentation.
- Ramadan-specific themes, labels and behaviour.
- Kemenag references and Indonesian religious conventions.
- BMKG API calls, Indonesian administrative-area code resolution and `bmkg.adm4` configuration.
- Indonesian-only copy, sample data, defaults and documentation.

### Preserve
- Media controls and artwork.
- WASAPI audio visualizer.
- Pomodoro timer.
- Split-island behaviour.
- Volume, battery, charging and device live activities.
- Fullscreen detection and auto-hide behaviour.
- Synchronized lyrics and LRCLIB integration.
- Clipboard history and screenshots.
- Windows notification integration.
- Drag-and-drop file shelf.
- Liquid Glass rendering.
- Network and peripheral activity.
- Mascot, meetings, calendar, system monitor and brightness feedback.

### Replace
- Weather provider: replace BMKG with a global provider that supports coordinates and automatic timezone handling. Open-Meteo is the current candidate; the integration must be implemented and validated before it is described as available.
- Location: retain generic location detection only where required by the international weather feature. Do not retain Indonesian regional lookup logic.

## Work plan

- [x] Verify the fork and write permissions.
- [x] Create the `refactor/global-english-cleanup` working branch.
- [x] Replace the Indonesian README with an English fork overview.
- [ ] Extract generic location detection from the legacy prayer module.
- [ ] Remove prayer and religious-calendar code and all dependent UI/state.
- [ ] Replace BMKG weather retrieval and remove Indonesian administrative lookups.
- [ ] Remove religious themes, settings, tray actions, sounds and demo sequences.
- [ ] Translate all remaining UI strings, menus, tooltips, dialogs, sample data and generated configuration comments.
- [ ] Remove obsolete configuration keys and migrate compatible existing settings safely.
- [ ] Audit the repository for Indonesian text and Indonesia-specific service references.
- [ ] Build on Windows and run the available self-tests.
- [ ] Review the final diff and document remaining limitations.

## Compatibility and safety notes

- Keep the original MIT licence and attribution intact.
- Do not rename persisted keys or change application data formats unless migration code is provided.
- Do not translate external API field names, protocol values, registry paths, Win32 identifiers or other machine-readable contracts.
- Do not remove generic location functionality merely because it currently lives in a file named `Prayer.cs`.
- Do not claim a feature has been removed or replaced until the source changes and build validation confirm it.
- Keep all implementation work on `refactor/global-english-cleanup`; do not modify the fork's `main` branch during the migration.

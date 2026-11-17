# Gili

A native Windows desktop companion inspired by Dynamic Island. Gili is built with C# and Win32/Windows APIs; it does not embed a browser engine.

> **Fork status: work in progress.** This fork is being adapted for an international, English-language audience. The legacy Indonesian prayer-time, religious-calendar and BMKG-specific functionality is scheduled for removal. The weather feature is planned to use a global provider. Until the code migration is complete, the current default branch still contains upstream functionality.

## Project goals

This fork aims to preserve Gili's general-purpose desktop features while removing religious and Indonesia-specific functionality.

### Planned feature set

| Feature | Description |
|---|---|
| Media controls | Track title, artist, artwork and playback controls for supported media apps and browser players. |
| Audio visualizer | Live audio spectrum using WASAPI loopback and FFT processing. |
| Pomodoro | Focus, short-break and long-break sessions with adjustable durations and completion chimes. |
| Split island | Display concurrent activities in separate island elements. |
| Live activity | Volume, charging state, low battery, track changes, microphone and camera activity. |
| Fullscreen awareness | Automatically get out of the way of fullscreen applications without stealing focus. |
| Synchronized lyrics | Karaoke-style lyrics and a scrollable teleprompter using [LRCLIB](https://lrclib.net). |
| Clipboard history | Recent text, image and file clips, with screenshot support. |
| Notifications | Surface supported Windows notifications and provide configurable privacy controls. |
| File shelf | Drag files onto the island for quick access and drag them back out to other applications. |
| International weather | Location-aware weather from a global provider; BMKG and Indonesian administrative-area lookups will not be used. |
| Liquid Glass | Backdrop blur, translucent surfaces and subtle highlights. |
| System activity | Battery, network and peripheral connection animations. |
| Desktop companion | Animated eye mascot, meeting reminders, system statistics and display-brightness feedback. |
| Settings | Display behaviour, themes, sizing, notifications and feature toggles. |

### Explicit removals

The fork will not include:
- Prayer-time calculations, prayer reminders, adhan/imsak alerts or related location settings.
- Hijri calendar, Ramadan mode, fasting or religious-calendar presentation.
- Kemenag or other religious authority integrations.
- BMKG weather endpoints or Indonesian administrative-area resolution.
- Indonesian-only defaults, labels, examples, documentation or user-facing strings.

## Platform and requirements

- Windows 10 or Windows 11, 64-bit.
- .NET Framework 4.8.
- Native C# application; no embedded browser engine.

## Build

The project uses the C# compiler available with .NET Framework 4.8 on Windows:

```cmd
build.cmd
```

To regenerate the application icon:

```powershell
powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
```

## Configuration and data

The application stores settings and runtime data under:

```text
%APPDATA%\Gili\
```

The migration will remove obsolete prayer and BMKG keys from the active configuration path. Existing user settings should be treated as untrusted input and retained only where they remain relevant to the fork.

## Repository layout

```text
src/                 C# application source
assets/              Application assets
tools/               Build-time utilities
build.cmd            Windows build script
LICENSE              Original MIT licence and attribution
```

## Development status

See [FORK_STATUS.md](FORK_STATUS.md) for the migration checklist and implementation notes.

## Credits and licence

This repository is a fork of [maulanarizky16/gili](https://github.com/maulanarizky16/gili).

The original project is distributed under the MIT License. The original copyright and licence notice are retained in [LICENSE](LICENSE). Changes in this fork will be documented separately.

"Dynamic Island" is a trademark of Apple Inc. This project is not affiliated with or endorsed by Apple.

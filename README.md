# Markd

Markd counts the days that matter. Mark a date and it counts **since** it (a first day, a streak, a birthday) or **until** it (a trip, a deadline), with milestones along the way. No ads, no account, no network calls: everything lives in one SQLite file on the device.

## Features

- Occasions counting since or until a date, with a live years / months / days / time breakdown
- One pinned occasion featured at the top of Home
- Milestones at day thresholds, with a "milestone reached" moment in the app and scheduled notifications on phones
- Categories with their own emoji and colour
- Month calendar of anchor dates and milestone dates, plus what is coming up next
- Light, dark and system themes
- Export and import as JSON, optionally encrypted

## Platforms

| Platform | UI |
|---|---|
| Android | Material 3 |
| Windows | Desktop layout: navigation rail, list and detail side by side, dialogs |
| iOS, Mac Catalyst | Build from the same code with the phone layout; the iOS design is not implemented yet |

## Build and run

Requires the .NET 10 SDK with the MAUI workloads (`android`, `ios`, `maccatalyst`, `maui-windows`).

```bash
# Windows desktop
dotnet build Markd/Markd.csproj -t:Run -f net10.0-windows10.0.19041.0

# Android (device or emulator connected)
dotnet build Markd/Markd.csproj -t:Run -f net10.0-android
```

## Tests

The test projects run on Microsoft.Testing.Platform (xUnit v3), so pass the project with `--project`:

```bash
dotnet test --project Markd.Core.Tests/Markd.Core.Tests.csproj
dotnet test --project Markd.Tests/Markd.Tests.csproj
```

## Project layout

- `Markd.Core` holds the UI-free domain, EF Core + SQLite data and services: occasions, milestones, categories, settings, export and import.
- `Markd` is the .NET MAUI app. View models and services are shared by both layouts:
  - `Desktop/` is the Windows layout (built only for Windows).
  - The pages in the project root and `Pages/`, with `Controls/Mobile/`, form the phone layout.
- `Markd.Core.Tests` runs the Core tests against in-memory SQLite.
- `Markd.Tests` tests view-model logic.

## Licence

MIT, see [LICENSE.txt](LICENSE.txt). Figtree and IBM Plex Mono are used under the SIL Open Font License 1.1.

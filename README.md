<p align="center">
  <img src="docs/assets/markd-hero.png" alt="Markd: every date that matters, counted to the second, since and until" width="820">
</p>

<h1 align="center">Markd</h1>

<p align="center">
  <a href="https://github.com/jame581/Markd/actions/workflows/ci.yml"><img alt="Tests" src="https://github.com/jame581/Markd/actions/workflows/ci.yml/badge.svg?branch=master"></a>
  <a href="https://github.com/jame581/Markd/actions/workflows/release.yml"><img alt="Release build" src="https://github.com/jame581/Markd/actions/workflows/release.yml/badge.svg"></a>
  <a href="https://github.com/jame581/Markd/releases/latest"><img alt="Latest release" src="https://img.shields.io/github/v/release/jame581/Markd?label=version"></a>
</p>

Markd counts the days that matter. Mark a date and it counts **since** it (a first day, a streak, a birthday) or **until** it (a trip, a deadline), with milestones along the way. No ads, no account, no network calls: everything lives in one SQLite file on the device.

## Features

- Occasions counting since or until a date, with a live years / months / days / time breakdown
- One pinned occasion featured at the top of Home
- Milestones at day thresholds, with a "milestone reached" moment in the app and scheduled notifications on phones
- Categories with their own emoji and colour
- Month calendar of anchor dates and milestone dates, plus what is coming up next
- Light, dark and system themes
- English, Czech, German and French, switched live from Settings or following the device language
- Backups: export to a password-protected `.markd` file (AES-256-GCM, key from PBKDF2-SHA256) or plain JSON, saved through the system file picker (so to Google Drive or OneDrive when their apps are installed) or sent with the share sheet. Restoring asks for the password and replaces the data on the device after a confirmation.

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

## CI and releases

- `.github/workflows/ci.yml` runs the tests on every push to `master` and every pull request.
- `.github/workflows/release.yml` runs when a `v*` tag is pushed. It runs the same tests, then builds:
  - **Android**: a signed `.apk` (for sideloading) and `.aab` (for Google Play)
  - **Windows**: a self-contained x64 `.zip` that runs without installing .NET or the Windows App SDK

The version lives in `ApplicationDisplayVersion` in `Markd/Markd.csproj`, and the app shows it in Settings and About. To release:

1. Set `ApplicationDisplayVersion` to the new version (for example `0.2.0`) and commit it.
2. Tag that commit with the same version and push the tag:

   ```bash
   git tag v0.2.0
   git push origin v0.2.0
   ```

The release stops if the tag does not match `ApplicationDisplayVersion`. The run number becomes the build number (the Android version code), and a draft GitHub release is created with the packages attached. Review it and publish it from the Releases page.

Android releases are signed with an upload key kept in repository secrets (Settings > Secrets and variables > Actions). Create the key once and keep a backup, because Google Play only accepts updates signed with the same key:

```bash
keytool -genkeypair -v -keystore markd.keystore -alias markd -keyalg RSA -keysize 2048 -validity 10000

# Upload the keystore as a secret with the GitHub CLI (bash, Git Bash or WSL)
base64 -w0 markd.keystore | gh secret set ANDROID_KEYSTORE_BASE64

# The same from PowerShell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("$PWD\markd.keystore")) | gh secret set ANDROID_KEYSTORE_BASE64
```

| Secret | Value |
|---|---|
| `ANDROID_KEYSTORE_BASE64` | the keystore, base64 encoded (above) |
| `ANDROID_KEYSTORE_PASSWORD` | the keystore password, which keytool also uses for the key |

The key alias defaults to `markd`. If you chose another, set it as the repository variable `ANDROID_KEY_ALIAS` (a variable, not a secret). Without the secrets the release stops before building Android.

## Project layout

- `Markd.Core` holds the UI-free domain, EF Core + SQLite data and services: occasions, milestones, categories, settings, export and import. `Localization/` has the UI text (`Strings.resx` plus one `Strings.<language>.resx` per translation), plural rules and language selection.
- `Markd` is the .NET MAUI app. View models and services are shared by both layouts:
  - `Desktop/` is the Windows layout (built only for Windows).
  - The pages in the project root and `Pages/`, with `Controls/Mobile/`, form the phone layout.
- `Markd.Core.Tests` runs the Core tests against in-memory SQLite.
- `Markd.Tests` tests view-model logic.

## Translations

All UI text lives in `Markd.Core/Localization/Strings.resx` (English). XAML shows it with `{loc:Tr Key}` and code with `Strings.Key`, so text updates live when the language changes. Counts use `_One` / `_Few` / `_Many` key families chosen by `Plural`.

The tests check that every translation has exactly the English keys and placeholders, and that every key used in XAML or code exists.

To add a language:

1. Copy `Strings.resx` to `Strings.<code>.resx` and translate the values.
2. Add the code to `LanguageSetting` (constant, `IsValid`, `Resolve`) and, if its plural rules differ, to `Plural.Select`.
3. Add its own name to the language picker in `SettingsViewModel` and to `Settings_LanguageSub`.
4. Add the code to `SatelliteResourceLanguages` in `Markd/Markd.csproj`, to `CFBundleLocalizations` in the iOS and Mac Catalyst `Info.plist`, and to the language list of `ResourceParityTests`.

## Licence

MIT, see [LICENSE.txt](LICENSE.txt). Figtree and IBM Plex Mono are used under the SIL Open Font License 1.1.

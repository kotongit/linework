# Linework for Windows

The WPF version of Linework. It reads the same library folder as the Mac app.

## Install

Run `Linework-Setup.exe`, from the `installer` artifact of the [Windows workflow](../.github/workflows/windows.yml). It installs for your account only, adds a Start menu entry, and needs nothing else on the PC.

The installer isn't signed, so Windows SmartScreen warns the first time. Select **More info**, then **Run anyway**.

Uninstalling removes the app and leaves your library alone. Settings stay in `%APPDATA%\Linework` and thumbnails in `%LOCALAPPDATA%\Linework`.

## Develop

On the Mac:

```bash
dotnet test windows/Linework.Core.Tests
```

runs all of Core, plus the store, photo and session checks, which have no WPF in them.

```bash
dotnet build windows/Linework.sln
```

compile-checks the app. Live behaviour is checked by `.github/workflows/windows.yml`, on Windows: Core tests, a harness driving real views against a scratch library, screenshots, and the installer. `Linework.exe --harness` in a Debug build needs `LINEWORK_LIBRARY` set to a scratch folder and refuses to run without it.

`scripts/extract-plan-json.py` regenerates the embedded plan from the Swift source, and `LINEWORK_WRITE_FIXTURES=1 swift test --filter SharedFixtureTests` regenerates `shared/fixtures`. A test fails if either drifts.

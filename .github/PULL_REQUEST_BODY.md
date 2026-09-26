# Pull Request: LibreScan Security v1.1.0 Maintenance Release & Documentation Updates

## Summary

This PR delivers the **v1.1.0** maintenance and stability release for **LibreScan Security**, an open-source, Windows-native antivirus frontend powered by the ClamAV engine.

---

## Key Deliverables & Changes

### 1. Bug Fixes & Test Suite Stabilisation
- **Fixed async deadlock in test teardown**: Wrapped `DeleteAllFromQuarantineAsync()` in `Task.Run()` inside `Dispose()` to prevent synchronous blocking on the test runner thread pool.
- **Fixed hidden UI prompt crash**: Added `skipConfirmation` parameter to `QuarantineSingleAsync()` so unit tests no longer hang on `MessageBox.Show()` during headless execution.
- **Expanded test suite**: 56 to 70 unit tests with improved quarantine operation coverage.

### 2. Version Bump to v1.1.0
- Updated version across all source and release files:
  - `LibreScan.csproj` (`<Version>1.1.0</Version>`)
  - `installer.iss` (`MyAppVersion "1.1.0"`)
  - `MainWindow.xaml` (UI sidebar version label)
  - `README.md`, all `docs/*.md`, and `release/*.md` files.

### 3. Documentation Overhaul
- **Repository URL Fix**: All links updated from placeholder `librescan/librescan-security` to the correct `richard-pius/LibreScan`.
- **Clone directory fix**: `cd librescan-security` changed to `cd LibreScan` across README, INSTALLATION, and DEVELOPER_GUIDE.
- **README.md**: Documentation section links now use absolute GitHub URLs for reliable rendering on any platform.
- **Release notes**: Refreshed with correct date, changelog, and SHA256 checksums.

### 4. Release Artifacts
- **`LibreScan_Setup_1.1.0.exe`**: Fresh Inno Setup installer compiled with updated UI version.
- **`LibreScan_v1.1.0_win-x64_portable.zip`**: New portable package from latest publish output.
- **`SHA256SUMS.txt`**: Regenerated with verified checksums for all release binaries.

---

## Verification & Testing

- [x] **70/70 Unit Tests Passing**: Full xUnit suite passes with 0 failures (`dotnet test`).
- [x] **Zero Build Errors/Warnings**: Clean build with .NET 10 on win-x64.
- [x] **Single-File Compilation**: `publish/LibreScan.exe` verified self-contained.
- [x] **Installer Compilation**: `LibreScan_Setup_1.1.0.exe` compiled successfully via Inno Setup 6.
- [x] **Portable Archive**: `LibreScan_v1.1.0_win-x64_portable.zip` generated with SHA256 checksum recorded.

---

## Release Asset Hashes

| File | SHA256 Checksum |
| :--- | :--- |
| `LibreScan_Setup_1.1.0.exe` | `5D6EC7326F0CEE6540FA8D893DBB6578B5D042E4271214589E26F49F268D2CE5` |
| `LibreScan_v1.1.0_win-x64_portable.zip` | `73F44A1636A7EA90B92D18050D2351A444F4270A4EF6F332842E26DE2A1444F4` |
| `LibreScan.exe` | `865DE8AA9A6BA7D3F01AFC9FDA62B0496FA7B4CFE22B6FC103D35DFED5E11C39` |

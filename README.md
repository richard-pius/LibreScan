# LibreScan Security

**Premium Windows-native antivirus frontend for the ClamAV engine.**

LibreScan Security provides a modern, dark-mode WPF interface for ClamAV — the open-source antivirus engine trusted by millions. It handles virus definition updates, quick and full scans, quarantine management, and silent system tray operation.

## Features

- 🛡️ **Quick Scan** — Scan critical system directories
- 🔍 **Full Scan** — Deep scan of all drives
- 🔄 **Auto-Update** — Smart virus definition updates (rate-limited to prevent IP bans)
- 📦 **Quarantine** — Safely isolate detected threats
- 🌙 **Dark Mode** — Premium dark UI designed for Windows 10/11
- 🔕 **Silent Boot** — Starts minimized to system tray on login
- 📦 **Single-File Deploy** — Ships as one self-contained `.exe`

## Requirements

- Windows 10 or later (x64)
- .NET 10 SDK (for building from source)
- [Inno Setup 6](https://jrsoftware.org/isdl.php) (for building the installer)

## Build from Source

```powershell
# Clone the repository
git clone https://github.com/librescan/librescan-security.git
cd librescan-security

# Run the full build pipeline
# Downloads ClamAV, compiles the app, and builds the installer
.\build_pipeline.ps1

# Build without ClamAV download (if binaries are already in clamav_bin/)
.\build_pipeline.ps1 -SkipClamAV

# Build without Inno Setup installer
.\build_pipeline.ps1 -SkipInstaller
```

## Project Structure

```
├── Assets/                  # Application icons and logos
├── clamav_bin/              # ClamAV engine binaries (auto-downloaded)
│   └── database/            # Virus definition files (.cvd)
├── src/LibreScan/           # WPF application source
│   ├── Services/            # ClamAV engine wrapper
│   ├── Models/              # Data models
│   ├── ViewModels/          # MVVM ViewModels
│   ├── Helpers/             # Tray icon manager, utilities
│   └── Resources/           # XAML styles and themes
├── build_pipeline.ps1       # Automated build script
├── installer.iss            # Inno Setup installer script
└── Quarantine/              # Isolated threat storage
```

## License

This project is licensed under the **GNU General Public License v2.0** — see the [LICENSE](LICENSE) file.

ClamAV is Copyright © Cisco Systems, Inc., licensed under GPL-2.0.

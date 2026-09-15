# LibreScan Security

[![License: GPL-2.0](https://img.shields.io/badge/License-GPL--2.0-blue.svg)](LICENSE)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20(x64)-0078D6.svg)](https://microsoft.com/windows)
[![Framework: .NET 10](https://img.shields.io/badge/Framework-.NET%2010%20WPF-512BD4.svg)](https://dotnet.microsoft.com/)
[![Tests: 60 Passed](https://img.shields.io/badge/Tests-60%20Passed%20(100%25)-brightgreen.svg)](tests/)
[![Engine: ClamAV](https://img.shields.io/badge/Engine-ClamAV®%20Portable-E24329.svg)](https://www.clamav.net/)

**LibreScan Security** is a premium, open-source Windows-native antivirus frontend for the **ClamAV®** engine. Designed with a sleek, high-contrast dark aesthetic inspired by modern developer tooling, LibreScan brings enterprise-grade scanning, automated definitions management, shell integration, and quarantine isolation to the Windows desktop.

---

## ✨ Features

- 🛡️ **Comprehensive Scanning Profiles**:
  - **Quick Scan**: Rapidly inspects user downloads, desktop, documents, startup, and temporary locations.
  - **Full Scan**: Deep recursive inspection across all fixed storage drives (`C:\`, `D:\`, etc.).
  - **Scan File**: Multi-selection file picker to scan specific executables, archives, or documents.
  - **Scan Folder**: Target any specific folder on disk.
  - **Scan Drive**: Visual drive browser with used/free capacity meters for internal, external, and USB flash drives.
- 🎯 **Interactive Drag & Drop Scanning**:
  - Drag files or directories straight from Windows Explorer into LibreScan with animated drop-zone feedback.
- 🔄 **Windows Explorer Context Menu**:
  - Right-click any file, folder, or drive in Windows Explorer and choose **"Scan with LibreScan"**.
- ⚡ **Single-Instance IPC (`WM_COPYDATA`)**:
  - Automatically forwards command-line and shell targets to running instances, restores from tray, and begins scanning immediately.
- 🔔 **System Tray & Balloon Notifications**:
  - Silent background operation with Windows notification balloons for completed scans, detected threats, and definition updates.
- 🗄️ **Quarantine Vault & Process Execution Termination**:
  - Forcibly terminates active malware processes holding kernel file execution locks before moving infected files into quarantine.
  - Granular threat management: quarantine individual threats, dismiss false positives, batch restore, or permanently empty quarantine.
- 💾 **Scan History Persistence**:
  - Preserves last scan timestamp, files scanned count, and threat summary across application restarts.
- 📋 **Activity Log Tools**:
  - Live throttled engine event logging with one-click **Clear** and timestamped **Export...** (`.txt`) capabilities.
- 🌙 **Sleek Modern Dark UI**:
  - High-performance WPF interface with smooth gradients, animated SVG-style vector shield, and responsive layout.
- 📦 **Zero External Runtimes Needed**:
  - Publishes as a self-contained single-file win-x64 executable.

---

## 💻 System Requirements

| Specification | Minimum Requirement | Recommended |
| :--- | :--- | :--- |
| **Operating System** | Windows 10 (64-bit, version 1809 or higher) | Windows 11 (64-bit, version 22H2 or higher) |
| **Architecture** | x64 (AMD64 / Intel 64-bit) | x64 (Multi-core) |
| **Memory (RAM)** | 2 GB RAM | 4 GB RAM or higher |
| **Storage** | 500 MB free space (for virus signature databases) | 2 GB free SSD space |
| **Permissions** | Standard User (Admin required for Setup Installer) | Administrator (recommended for locked file handling) |
| **Network** | Internet connection for initial definitions download | Broadband connection for daily signature updates |

---

## 🚀 Installation & Getting Started

### Option 1: Portable Release (Zero Installation)
1. Download `LibreScan_v1.0.0_win-x64_portable.zip` from [Releases](https://github.com/librescan/librescan-security/releases).
2. Extract the `.zip` to any preferred directory.
3. Launch `LibreScan.exe`.
4. On first run, LibreScan will automatically download the latest virus definitions from ClamAV servers.

### Option 2: Windows Setup Installer
1. Download `LibreScan_Setup_1.0.0.exe` from [Releases](https://github.com/librescan/librescan-security/releases).
2. Run the installer with administrator privileges.
3. Select desired integration options (Desktop shortcut, Windows auto-start, Explorer context menu).
4. Launch LibreScan from the Start Menu or Desktop.

For detailed instructions, see the [Installation Guide](docs/INSTALLATION.md).

---

## 🛠️ Building from Source

### Prerequisites
- [.NET 10 SDK (x64)](https://dotnet.microsoft.com/download/dotnet/10.0)
- PowerShell 5.1 or PowerShell 7+
- (Optional) [Inno Setup 6](https://jrsoftware.org/isdl.php) for building the installer.

### Build Steps
```powershell
# Clone the repository
git clone https://github.com/librescan/librescan-security.git
cd librescan-security

# Run the automated build pipeline
# Downloads ClamAV portable binaries, compiles the app, and prepares release output
.\build_pipeline.ps1 -SkipInstaller

# Or compile via standard dotnet CLI
dotnet build src\LibreScan\LibreScan.csproj -c Release

# Run automated tests (60 tests)
dotnet test
```

For developer documentation and contribution guidelines, see the [Developer Guide](docs/DEVELOPER_GUIDE.md).

---

## 📚 Documentation

- 📖 [User Guide](docs/USER_GUIDE.md) — Comprehensive feature walkthrough and operational guide.
- 🏗️ [Architecture & Technical Design](docs/ARCHITECTURE.md) — IPC, process management, file-locking elimination, and quarantine internals.
- 💾 [Installation Guide](docs/INSTALLATION.md) — System requirements, portable vs. installer setups, and unattended updates.
- 💻 [Developer Guide](docs/DEVELOPER_GUIDE.md) — Code style, testing guidelines, and contributing.
- ⚖️ [Third-Party Licenses](THIRD_PARTY_LICENSES.md) — Attribution and licensing for ClamAV®, .NET, and typography.

---

## ⚖️ License & Legal Notices

LibreScan Security is open-source software licensed under the **GNU General Public License v2.0 (GPL-2.0)**. See the [LICENSE](LICENSE) file for complete details.

### Third-Party Trademarks and Attributions
- **ClamAV®** is a registered trademark of Cisco Systems, Inc.
- LibreScan Security is an independent community project and is not affiliated with, endorsed by, or sponsored by Cisco Systems, Inc.
- All application icons, graphical assets, and vector geometry are 100% original and free from proprietary copyright restrictions. See [THIRD_PARTY_LICENSES.md](THIRD_PARTY_LICENSES.md).

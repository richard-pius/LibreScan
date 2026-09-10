# Installation Guide — LibreScan Security

LibreScan Security is a modern, lightweight, Windows-native frontend for the ClamAV® antivirus engine. This guide covers system requirements, installation methods, initial configuration, and troubleshooting.

---

## System Requirements

| Requirement | Minimum | Recommended |
| :--- | :--- | :--- |
| **Operating System** | Windows 10 (64-bit, version 1809+) | Windows 11 (64-bit, 22H2 or newer) |
| **Processor** | 64-bit (x64) Intel / AMD dual-core | Quad-core 2.5 GHz or higher |
| **Memory (RAM)** | 2 GB RAM | 4 GB RAM or more |
| **Disk Space** | ~500 MB free space (for virus definitions) | 2 GB free SSD space |
| **Display** | 1280 × 720 resolution | 1920 × 1080 resolution or higher |
| **Permissions** | Standard User (Admin for Inno Setup installer) | Administrator (for locked OS file quarantine) |
| **Network** | Internet connection for initial definitions download | Broadband connection for daily signature updates |

---

## Installation Methods

### Method 1: Portable Release (Recommended for Quick Use)

1. Download the latest `LibreScan_v1.0.0_win-x64_portable.zip` from [GitHub Releases](https://github.com/librescan/librescan-security/releases).
2. Extract the archive to a folder of your choice (e.g., `C:\Program Files\LibreScan` or `C:\Tools\LibreScan`).
3. Run `LibreScan.exe`.
4. On first launch:
   - If virus definitions (`clamav_bin/database`) are not yet downloaded, LibreScan will automatically start downloading the latest ClamAV signatures (`daily.cvd` and `main.cvd`).
   - The status bar will show **"Updating Definitions..."**. Once complete, the shield icon will change to green with **"System Protected"**.

---

### Method 2: Windows Installer (`.exe`)

The Windows Setup Wizard provides system-wide integration:
- Adds **"Scan with LibreScan"** to the Windows Explorer right-click context menu for all files, folders, and drives.
- Creates Start Menu and Desktop shortcuts.
- Configures automatic silent startup to the system tray (`--startup`) on Windows login.
- Sets appropriate read/write permissions on the Quarantine and Database folders.

#### Steps:
1. Download `LibreScan_Setup_1.0.0.exe` from [GitHub Releases](https://github.com/librescan/librescan-security/releases).
2. Run the installer (Administrator permissions required).
3. Select your desired options:
   - [x] **Create a desktop shortcut**
   - [x] **Start LibreScan Security with Windows** (starts minimized in tray)
   - [x] **Add "Scan with LibreScan" to Windows Explorer context menu**
4. Click **Install**.
5. When complete, launch LibreScan from the Start Menu or Desktop.

---

### Method 3: Building from Source

If you prefer building from source code:

#### Prerequisites:
- [.NET 10 SDK (x64)](https://dotnet.microsoft.com/download)
- PowerShell 5.1 or PowerShell 7+
- (Optional) [Inno Setup 6](https://jrsoftware.org/isdl.php) if you want to compile the installer.

#### Build Commands:
```powershell
# 1. Clone the repository
git clone https://github.com/librescan/librescan-security.git
cd librescan-security

# 2. Run the automated build pipeline
# This downloads ClamAV portable binaries, publishes the WPF single-file binary,
# and prepares the release directory.
.\build_pipeline.ps1 -SkipInstaller

# 3. The compiled binary is available in:
# publish\LibreScan.exe
```

---

## Post-Installation Verification

1. **Verify Engine Connection**:
   - Open LibreScan.
   - Look at the sidebar: the database timestamp should show the current date.
   - Look at the main shield: it should show **"System Protected"**.
2. **Perform a Test Scan**:
   - Click the **Quick Scan** action card or drag a test folder into the window.
   - The scanning animation, elapsed time counter, and file counter will update live.
   - At completion, a notification balloon will appear in the Windows system tray.

---

## Uninstalling LibreScan

- **If installed via Setup**:
  - Open **Windows Settings > Apps > Installed apps**.
  - Search for **LibreScan Security** and click **Uninstall**.
  - The uninstaller will terminate running instances, cleanly remove context menu registry entries, autostart entries, and shortcuts.
- **If using Portable**:
  - Close LibreScan from the system tray (right-click icon > **Exit**).
  - Delete the extracted folder.
  - (Optional) Remove scan history by deleting `%LocalAppData%\LibreScan`.

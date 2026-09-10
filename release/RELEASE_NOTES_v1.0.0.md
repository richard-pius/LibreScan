# LibreScan Security v1.0.0 Release Notes

**Release Date:** September 10, 2026  
**Target Platform:** Windows 10 / Windows 11 (x64)  
**License:** GNU General Public License v2.0 (GPL-2.0)  

---

## 🚀 Overview

We are proud to announce the initial production release of **LibreScan Security v1.0.0**, a modern, open-source Windows-native antivirus frontend for the **ClamAV®** engine.

Built on .NET 10 and WPF with a high-contrast dark aesthetic, LibreScan Security delivers fast, reliable, and user-friendly protection without telemetry, ads, or proprietary bloatware.

---

## ✨ Key Features

- **5 Versatile Scanning Modes**:
  - **Quick Scan**: Key user directories (Downloads, Desktop, Documents, Temp, Startup).
  - **Full Scan**: Comprehensive deep scan across all fixed storage drives.
  - **Scan File**: Multi-file picker for targeted inspection of downloads or executables.
  - **Scan Folder**: Custom directory targeting.
  - **Scan Drive**: Dedicated storage disk picker with visual used/free capacity meters.
- **Drag & Drop Scanning**: Drag any file or directory directly onto LibreScan for instant scanning.
- **Windows Explorer Right-Click Integration**: "Scan with LibreScan" available on files, folders, and drives.
- **Single-Instance IPC (`WM_COPYDATA`)**: Seamlessly forwards scan targets from Windows Explorer to existing running instances.
- **System Tray Operation & Balloon Notifications**: Silent background protection with notification alerts on completed scans and updates.
- **Malware Process Lock Termination**: Forcibly terminates active malware processes holding kernel execution locks (`ERROR_SHARING_VIOLATION`) before quarantine isolation.
- **Quarantine Vault**: Granular threat controls (isolate, dismiss, individual restore, batch restore, or permanent wipe).
- **Persistent Scan History**: Preserves scan timestamps, scanned counts, and threat stats across app restarts.
- **Activity Log Export**: Real-time engine log with clear and timestamped export (`.txt`).
- **Zero Runtime Dependencies**: Packaged as a self-contained single-file win-x64 binary.

---

## 📦 Release Assets & Checksums

| File | Description | SHA256 Checksum |
| :--- | :--- | :--- |
| `LibreScan.exe` | Standalone single-file self-contained executable (win-x64) | `5D6C66EE56AA61F123697CE7C7855CC1DA775AAD34C8C24C530CD7F37CB9C525` |
| `LibreScan_v1.0.0_win-x64_portable.zip` | Complete portable package with ClamAV engine binaries | `2F34727427AED98D57C949CE4ED7540ACE28EC7E593FD85636CDC55935C1397C` |
| `LibreScan_Setup_1.0.0.exe` | Windows Setup installer script (`installer/LibreScan.iss`) | *(Compile with Inno Setup)* |

---

## 💻 System Requirements

- **Operating System**: Windows 10 (64-bit, 1809+) or Windows 11 (64-bit)
- **Processor**: 64-bit Intel or AMD processor
- **Memory**: 2 GB RAM minimum (4 GB recommended)
- **Disk Space**: ~500 MB free space for virus definitions

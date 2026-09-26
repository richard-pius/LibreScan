# LibreScan Security v1.1.0 Release Notes

**Release Date:** September 10, 2026  
**Target Platform:** Windows 10 / Windows 11 (x64)  
**License:** GNU General Public License v2.0 (GPL-2.0)  

---

## ðŸš€ Overview

We are proud to announce the initial production release of **LibreScan Security v1.1.0**, a modern, open-source Windows-native antivirus frontend for the **ClamAVÂ®** engine.

Built on .NET 10 and WPF with a high-contrast dark aesthetic, LibreScan Security delivers fast, reliable, and user-friendly protection without telemetry, ads, or proprietary bloatware.

---

## âœ¨ Key Features

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

## ðŸ“¦ Release Assets & Checksums

| File | Description | SHA256 Checksum |
| :--- | :--- | :--- |
| `LibreScan_Setup_1.1.0.exe` | Windows Setup Installer with ClamAV & Definitions bundled | `8363E0EFDBBEABC2DC4EA10F949B29EE5371CEC8BC93196AF0DBD839F26FC409` |
| `LibreScan_v1.1.0_win-x64_portable.zip` | Complete portable package with ClamAV engine & definitions | `978D6CB8D68E7109A0506D6FD79EC14A4E340F3E493B142D3306C7FFDB9980A4` |
| `LibreScan.exe` | Standalone single-file self-contained executable (win-x64) | `865DE8AA9A6BA7D3F01AFC9FDA62B0496FA7B4CFE22B6FC103D35DFED5E11C39` |

---

## ðŸ’» System Requirements

- **Operating System**: Windows 10 (64-bit, 1809+) or Windows 11 (64-bit)
- **Processor**: 64-bit Intel or AMD processor
- **Memory**: 2 GB RAM minimum (4 GB recommended)
- **Disk Space**: ~500 MB free space for virus definitions


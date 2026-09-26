# LibreScan Security v1.1.0 Release Notes

**Release Date:** September 26, 2026
**Target Platform:** Windows 10 / Windows 11 (x64)
**License:** GNU General Public License v2.0 (GPL-2.0)

---

## Overview

We are proud to announce **LibreScan Security v1.1.0**, a maintenance and stability release for our modern, open-source Windows-native antivirus frontend powered by the **ClamAV** engine.

Built on .NET 10 and WPF with a high-contrast dark aesthetic, LibreScan Security delivers fast, reliable, and user-friendly protection without telemetry, ads, or proprietary bloatware.

---

## What's New in v1.1.0

- **Bug Fixes**: Fixed test suite deadlocks caused by synchronous blocking on async disposal and hidden UI confirmation prompts during headless test execution.
- **Test Suite Expansion**: Expanded from 56 to 70 unit tests with improved coverage for quarantine operations and edge cases.
- **Documentation Overhaul**: Updated all documentation with correct repository URLs, build instructions, and version references.
- **Release Pipeline Improvements**: Refreshed build pipeline with updated installer and portable package generation.

---

## Key Features

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

## Release Assets & Checksums

| File | Description | SHA256 Checksum |
| :--- | :--- | :--- |
| `LibreScan_Setup_1.1.0.exe` | Windows Setup Installer with ClamAV & Definitions bundled | `5D6EC7326F0CEE6540FA8D893DBB6578B5D042E4271214589E26F49F268D2CE5` |
| `LibreScan_v1.1.0_win-x64_portable.zip` | Complete portable package with ClamAV engine & definitions | `73F44A1636A7EA90B92D18050D2351A444F4270A4EF6F332842E26DE2A1444F4` |
| `LibreScan.exe` | Standalone single-file self-contained executable (win-x64) | `865DE8AA9A6BA7D3F01AFC9FDA62B0496FA7B4CFE22B6FC103D35DFED5E11C39` |

---

## System Requirements

- **Operating System**: Windows 10 (64-bit, 1809+) or Windows 11 (64-bit)
- **Processor**: 64-bit Intel or AMD processor
- **Memory**: 2 GB RAM minimum (4 GB recommended)
- **Disk Space**: ~500 MB free space for virus definitions

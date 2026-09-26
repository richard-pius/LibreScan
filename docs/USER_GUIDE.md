# User Guide â€” LibreScan Security

Welcome to the LibreScan Security user guide. This document explains how to use all features of LibreScan to protect your computer.

---

## 1. Dashboard Overview

When you launch LibreScan Security, the Dashboard displays:
- **Status Shield**: Shows current protection status:
  - ðŸŸ¢ **System Protected**: Virus definitions are current, and no active threats are pending.
  - ðŸ”µ **Running Scan**: Scanning files with live file count and elapsed time.
  - ðŸŸ  **Warning / Needs Update**: Virus definitions are older than 24 hours or missing.
  - ðŸ”´ **Threats Found**: Malware detected. Prompt to review and quarantine threats.
- **Sidebar Details**:
  - Engine state and database update timestamp.
  - Last scan summary (files scanned, threats found, date/time).
  - Navigation buttons (**Dashboard**, **Quarantine**).

---

## 2. Scanning Options

LibreScan provides 5 flexible scanning profiles:

### âš¡ Quick Scan
- Scans common infection vectors and active user locations:
  - User Downloads, Desktop, and Documents folders.
  - Windows Startup directories.
  - Windows Temp directories.
- Typically completes in under 60 seconds.

### ðŸ” Full Scan
- Performs an exhaustive deep scan across all fixed storage drives connected to your system (`C:\`, `D:\`, etc.).
- Excludes system swap/hibernation files (`pagefile.sys`, `hiberfil.sys`) to avoid unnecessary lock warnings.

### ðŸ“„ Scan File
- Click the **Scan File** card to open the Windows file picker.
- Supports multi-selection: you can select one or several specific files (executables, archives, documents) to scan immediately.

### ðŸ“ Scan Folder
- Click the **Scan Folder** card to select any specific directory or folder on your computer.

### ðŸ’¾ Scan Drive (USB & Disks)
- Click the **Scan Drive** card to open the Drive Selection panel.
- Displays all ready fixed, removable USB flash drives, and external disks with used/free capacity bars.
- Click **Scan Drive** on any item to scan that specific disk.

---

## 3. Drag & Drop Scanning

You can drag any file or folder from Windows Explorer directly onto LibreScan:
1. Drag one or multiple files/folders into the LibreScan window.
2. A stylish dashed-border **"Drop to Scan"** overlay will appear.
3. Release the mouse: LibreScan immediately switches to the Dashboard, activates the scanning engine, and reports findings.

---

## 4. Windows Explorer Right-Click Integration

When installed via the Setup installer:
- Right-click any file, folder, or drive in Windows Explorer.
- Click **"Scan with LibreScan"**.
- LibreScan opens, unhides from the system tray, brings the window to the front, and begins scanning your selected item immediately.

---

## 5. Threat Detection & Quarantine

When malware or suspicious files are discovered:
1. The status shield turns **Red (Danger)**.
2. The **Detected Threats** list appears on the Dashboard displaying threat names and file locations.
3. You have two options for each threat:
   - **Quarantine**: Forcibly terminates any process holding an execution lock on the file and moves it into the secure Quarantine vault.
   - **Dismiss**: Ignores the detection for this session if you recognize it as a false positive.
4. Or click **"Quarantine All"** to isolate all detected threats at once.

### Managing Quarantined Items
Switch to the **Quarantine** tab from the sidebar:
- **Restore**: Restores a quarantined file back to its original location on disk.
- **Delete**: Permanently destroys the quarantined file.
- **Restore All**: Batch-restores all quarantined files with a single confirmation.
- **Empty Quarantine**: Permanently purges all files from the quarantine vault and cleans up disk space.

---

## 6. System Tray & Background Notifications

- Closing the LibreScan window minimizes it to the Windows System Tray (near the clock).
- Right-click the tray icon to:
  - **Open LibreScan Security**
  - **Quick Scan**
  - **Update Definitions**
  - **Exit**
- LibreScan shows Windows notification balloons when:
  - A scan finishes cleanly.
  - Threats are detected (clicking the notification opens the threat review).
  - Virus definitions finish updating in the background.

---

## 7. Activity Log & Export

- The Dashboard contains a real-time **Activity Log** displaying engine events, scanned items, and update details.
- **Clear**: Clears the visible log window.
- **Export...**: Saves the complete log history to a timestamped `.txt` file for technical support or record-keeping.


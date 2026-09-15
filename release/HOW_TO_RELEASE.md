# How to Publish v1.0.0 on GitHub Releases

All release binaries and metadata have been compiled and verified in this `release/` directory.

---

## 📋 Quick Steps for GitHub Releases

1. Navigate to: **[https://github.com/richard-pius/LibreScan/releases/new](https://github.com/richard-pius/LibreScan/releases/new)**
2. **Choose a tag**: Type `v1.0.0` and select **Create new tag: v1.0.0 on publish**.
3. **Target branch**: Select `main`.
4. **Release title**: Enter:
   ```text
   LibreScan Security v1.0.0
   ```
5. **Release description**: Open and copy the entire text of [`RELEASE_NOTES_v1.0.0.md`](RELEASE_NOTES_v1.0.0.md) and paste it into the description box.
6. **Attach binary files** (drag and drop from this `release/` folder):
   - `LibreScan_Setup_1.0.0.exe` *(Windows Setup Installer with ClamAV & Definitions bundled)*
   - `LibreScan_v1.0.0_win-x64_portable.zip` *(Complete Portable Package with ClamAV bundled)*
   - `LibreScan.exe` *(Standalone single-file executable)*
   - `SHA256SUMS.txt` *(Cryptographic checksums file)*
7. Click **Publish release**.

---

## 📦 Verified Assets in this Directory

| File | Size | SHA256 Checksum |
| :--- | :--- | :--- |
| `LibreScan_Setup_1.0.0.exe` | ~163.76 MB | `8363E0EFDBBEABC2DC4EA10F949B29EE5371CEC8BC93196AF0DBD839F26FC409` |
| `LibreScan_v1.0.0_win-x64_portable.zip` | ~212.81 MB | `978D6CB8D68E7109A0506D6FD79EC14A4E340F3E493B142D3306C7FFDB9980A4` |
| `LibreScan.exe` | ~165.10 MB | `865DE8AA9A6BA7D3F01AFC9FDA62B0496FA7B4CFE22B6FC103D35DFED5E11C39` |
| `SHA256SUMS.txt` | 279 B | *(Checksum manifest)* |
| `RELEASE_NOTES_v1.0.0.md` | ~2.9 KB | *(Formatted Markdown release notes)* |

# How to Publish v1.1.0 on GitHub Releases

All release binaries and metadata have been compiled and verified in this `release/` directory.

---

## Quick Steps for GitHub Releases

1. Navigate to: **[https://github.com/richard-pius/LibreScan/releases/new](https://github.com/richard-pius/LibreScan/releases/new)**
2. **Choose a tag**: Type `v1.1.0` and select **Create new tag: v1.1.0 on publish**.
3. **Target branch**: Select `main`.
4. **Release title**: Enter:
   ```text
   LibreScan Security v1.1.0
   ```
5. **Release description**: Open and copy the entire text of [`RELEASE_NOTES_v1.1.0.md`](RELEASE_NOTES_v1.1.0.md) and paste it into the description box.
6. **Attach binary files** (drag and drop from this `release/` folder):
   - `LibreScan_Setup_1.1.0.exe` *(Windows Setup Installer with ClamAV & Definitions bundled)*
   - `LibreScan_v1.1.0_win-x64_portable.zip` *(Complete Portable Package with ClamAV bundled)*
   - `LibreScan.exe` *(Standalone single-file executable)*
   - `SHA256SUMS.txt` *(Cryptographic checksums file)*
7. Click **Publish release**.

---

## Verified Assets in this Directory

| File | Size | SHA256 Checksum |
| :--- | :--- | :--- |
| `LibreScan_Setup_1.1.0.exe` | ~168.68 MB | `1AD1A516D268FDCEEB65C84CAFC7B3A4ACA6D043622214A54773712ED2F0F490` |
| `LibreScan_v1.1.0_win-x64_portable.zip` | ~211.14 MB | `7C2FCF89ECBDC5713BE3EF37600E654E886D274EBB26B659810878B84312DBC6` |
| `LibreScan.exe` | ~165.11 MB | `251CA551ED0515BB25A0CA596D100A02D816AE1ADFADDB9CF1F7A94AD5EB1C53` |
| `SHA256SUMS.txt` | < 1 KB | *(Checksum manifest)* |
| `RELEASE_NOTES_v1.1.0.md` | ~3 KB | *(Formatted Markdown release notes)* |

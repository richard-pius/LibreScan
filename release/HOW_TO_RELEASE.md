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
| `LibreScan_Setup_1.1.0.exe` | ~163.79 MB | `5D6EC7326F0CEE6540FA8D893DBB6578B5D042E4271214589E26F49F268D2CE5` |
| `LibreScan_v1.1.0_win-x64_portable.zip` | ~212.89 MB | `73F44A1636A7EA90B92D18050D2351A444F4270A4EF6F332842E26DE2A1444F4` |
| `LibreScan.exe` | ~165.10 MB | `865DE8AA9A6BA7D3F01AFC9FDA62B0496FA7B4CFE22B6FC103D35DFED5E11C39` |
| `SHA256SUMS.txt` | < 1 KB | *(Checksum manifest)* |
| `RELEASE_NOTES_v1.1.0.md` | ~3 KB | *(Formatted Markdown release notes)* |

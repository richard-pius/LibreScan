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
| `LibreScan_Setup_1.0.0.exe` | ~163.75 MB | `D329573D351B48982902491C1D866BB071656A9E06532BA639CD85C26438490D` |
| `LibreScan_v1.0.0_win-x64_portable.zip` | ~212.81 MB | `F29C1C2B487C122ED3999B48051D7E51BEA78C7D6F053918A086BD029BC5E4B0` |
| `LibreScan.exe` | ~165.10 MB | `F4289E4570B911594D09C226D3606DEDFE5EC772A7764BF2EF0A1D22972EA7EE` |
| `SHA256SUMS.txt` | 279 B | *(Checksum manifest)* |
| `RELEASE_NOTES_v1.0.0.md` | ~2.9 KB | *(Formatted Markdown release notes)* |

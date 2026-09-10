# Developer & Contributor Guide — LibreScan Security

Thank you for contributing to LibreScan Security! This document outlines development setup, building, testing, coding conventions, and pull request guidelines.

---

## Development Prerequisites

- **OS**: Windows 10/11 (x64)
- **SDK**: [.NET 10 SDK (x64)](https://dotnet.microsoft.com/download/dotnet/10.0)
- **IDE**: Visual Studio 2022 / 2026, JetBrains Rider, or VS Code with C# Dev Kit.
- **Tools**:
  - Git for Windows
  - PowerShell 5.1 or 7+
  - (Optional) Inno Setup 6 (for packaging installer)

---

## Repository Setup & Building

```powershell
# 1. Clone the repository
git clone https://github.com/librescan/librescan-security.git
cd librescan-security

# 2. Build the solution
dotnet build -v minimal

# 3. Run the automated test suite
dotnet test

# 4. Run the application locally
dotnet run --project src\LibreScan\LibreScan.csproj
```

---

## Testing Guidelines

LibreScan uses **xUnit** for unit and integration tests under `tests/LibreScan.Tests/`.
- Tests run sequentially via `[assembly: CollectionBehavior(DisableTestParallelization = true)]` to prevent test races against filesystem quarantine directories.
- Always run `dotnet test` before submitting code changes:
  ```powershell
  dotnet test
  ```
- All 56 existing unit tests must pass with 0 errors.

---

## Coding Standards

1. **C# 13 & Modern .NET**:
   - Use file-scoped namespaces (`namespace LibreScan;`).
   - Use primary constructors, record types, and pattern matching where appropriate.
   - Use `[LibraryImport]` with source-generated P/Invoke over legacy `[DllImport]`.
   - Use `[GeneratedRegex]` for high-performance regexes.
2. **WPF & MVVM**:
   - Keep code-behind in `MainWindow.xaml.cs` minimal (restricted to pure view lifecycle, P/Invoke, and drag/drop events).
   - All presentation logic and commands belong in `MainViewModel.cs`.
   - When modifying `ObservableCollection`s from background worker tasks, always wrap in `RunOnUI(...)`.
   - Re-evaluate commands using `CommandManager.InvalidateRequerySuggested` via `InvalidateCommands()`.
3. **Defensive I/O**:
   - Never assume paths exist or have normal permissions; anticipate `ReadOnly` files, transient `IOException`s, and `UnauthorizedAccessException`.
   - Use retries and rollback handlers for atomic disk state commits.
4. **License Compatibility**:
   - Keep the project licensed under **GPL-2.0**.
   - Do NOT introduce dependencies, assets, or code with proprietary, copyleft-incompatible, or non-commercial restriction licenses.

---

## Submitting Pull Requests

1. Fork the repository and create a descriptive feature branch:
   ```bash
   git checkout -b feature/your-feature-name
   ```
2. Commit your changes with clear, structured commit messages.
3. Verify `dotnet test` and `dotnet build` are 100% clean.
4. Push your branch to GitHub and open a Pull Request.

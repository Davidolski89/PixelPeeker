# Code navigation

- `CODEBASE_MAP.md` is the primary navigation guide for locating code.
- Consult its relevant sections before exploring unfamiliar parts of the application.
- Read `README.md` only when application behavior or user-facing functionality needs clarification.
- Use targeted searches to verify locations rather than scanning the whole repository.
- Update `CODEBASE_MAP.md` only when code structure or feature locations change.
- Do not regenerate the entire navigation guide for small modifications.

# Portable releases

- Always create/update `artifacts/portable-win-x64` for every completed portable release, in addition to its versioned `artifacts/portable-win-x64-<version>` folder.
- Use `Publish.ps1` to keep both folders current. A versioned build alone does not complete publishing.

---
name: frontend_packages_management
description: Guidelines for managing and updating front-end web packages (libman, npm, and manually vendored libraries) in the MobileGnollHackLogger and Overseer projects. Covers Bootstrap, jQuery, and wwwroot/lib dependencies.
---

# Front-End Packages Management

The MobileGnollHackLogger project relies on a combination of Library Manager (`libman.json`) and manual downloads to manage front-end web packages located in `wwwroot/lib/`. 

## 1. Library Manager (`libman`)
We use `libman` to manage the following standard dependencies from `cdnjs`:
- **Twitter Bootstrap** (`bootstrap`)
- **Bootstrap Icons** (`bootstrap-icons`)
- **jQuery** (`jquery`)
- **jQuery Validation** (`jquery-validate`)
- **jQuery Validation Unobtrusive** (`jquery-validation-unobtrusive`)

### Updating `libman` Packages
To update these packages:
1. Open `MobileGnollHackLogger/libman.json`.
2. Update the version string in the `"library"` field to the latest available version on `cdnjs`.
3. Verify that the `"files"` array still correctly lists the minified JS/CSS and font files required by the project. The `"files"` array ensures we only download the files we actually use, rather than syncing thousands of unused source maps, SCSS files, localization variants, or SVG icons.
4. Run the following command from the `MobileGnollHackLogger` project root to restore the files:
   ```bash
   dotnet tool install -g Microsoft.Web.LibraryManager.Cli
   libman restore
   ```

## 2. DataTables
DataTables is managed via **manual download** from the official CDN (https://cdn.datatables.net/). We use **DataTables 3.x** and strictly avoid legacy combined bundles (`datatables.min.js`).

### Updating DataTables
To update DataTables to a newer version:
1. Define the target version (e.g., `3.0.x`).
2. Run PowerShell commands to download the separated core JS/CSS and Bootstrap 5 styling integration directly into `wwwroot/lib/datatables/`:
   ```powershell
   Invoke-WebRequest -Uri "https://cdn.datatables.net/<VERSION>/js/dataTables.min.js" -OutFile "wwwroot/lib/datatables/js/dataTables.min.js"
   Invoke-WebRequest -Uri "https://cdn.datatables.net/<VERSION>/js/dataTables.bootstrap5.min.js" -OutFile "wwwroot/lib/datatables/js/dataTables.bootstrap5.min.js"
   Invoke-WebRequest -Uri "https://cdn.datatables.net/<VERSION>/css/dataTables.bootstrap5.min.css" -OutFile "wwwroot/lib/datatables/css/dataTables.bootstrap5.min.css"
   ```

### DataTables Conventions
- **VanillaJS Initialization:** Always initialize DataTables using the `new DataTable(element, options)` constructor, rather than the legacy jQuery `$(...).dataTable()` wrapper.
- **CSS Overrides:** Custom DataTables styling overrides are located in `wwwroot/css/site2.scss` (around line 738). When updating DataTables, ensure you are overriding the modern `dt-*` classes (e.g., `.dt-container`, `.dt-search`, `.dt-paging`), rather than legacy `dataTables_*` classes.
- **SCSS Compilation:** If you modify `site2.scss`, you MUST recompile it using `npx sass` (refer to the `scss_compilation` skill).

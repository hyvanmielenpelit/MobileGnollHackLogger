# MobileGnollHackLogger Project Rules

These rules apply to all AI-assisted development on the MobileGnollHackLogger codebase.

## Project Overview

MobileGnollHackLogger is an ASP.NET Core web application that logs, processes, and displays game logs, leaderboards, and user accounts for GnollHack.

## SCSS and CSS Conventions

### Rules for Style Sheets
- **Do NOT modify CSS files directly** if there is a corresponding SCSS file (e.g., `wwwroot/css/site2.scss` generates `wwwroot/css/site2.css` and `wwwroot/css/site2.min.css`).
- **Modify only the SCSS file** (`.scss`) for styling updates.
- **Compile SCSS files** using `npx sass` to regenerate the corresponding CSS and minified CSS files.

### Compilation Commands
To compile SCSS files:
- Standard CSS:
  ```bash
  npx sass wwwroot/css/site2.scss wwwroot/css/site2.css
  ```
- Minified CSS:
  ```bash
  npx sass wwwroot/css/site2.scss wwwroot/css/site2.min.css --style compressed
  ```

## Image Conventions

### Rules for Image Files
- **WebP format**: Convert JPG and PNG images to WebP to optimize web asset performance.
- **Conversion quality**: When converting images to WebP, always use a compression quality of **85** (e.g., `quality=85` in Pillow or `-q 85` in cwebp).

## File Organization

| Area | Location |
|------|----------|
| Razor Pages | `MobileGnollHackLogger/Pages/` |
| Stylesheets (SCSS) | `MobileGnollHackLogger/wwwroot/css/site2.scss` |
| Generated Stylesheets (CSS) | `MobileGnollHackLogger/wwwroot/css/site2.css` & `site2.min.css` |
| Images | `MobileGnollHackLogger/wwwroot/img/` |
| Program Entry & Startup | `MobileGnollHackLogger/Program.cs` |

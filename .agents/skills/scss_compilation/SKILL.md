---
name: scss_compilation
description: Guidelines for styling modifications, ensuring CSS files are not modified directly when SCSS source files exist, and compiling using npx sass.
---

# SCSS Compilation and Styling Guidelines

## Guidelines
- **SCSS Source Files**: All primary style modifications must be done in the `.scss` files, such as `MobileGnollHackLogger/wwwroot/css/site2.scss`.
- **Generated CSS Files**: Files like `site2.css` and `site2.min.css` are automatically generated or compiled. Do NOT modify these files directly.
- **Compilation**: After updating `.scss` files, compile them to CSS and minified CSS using `npx sass` from the `MobileGnollHackLogger/MobileGnollHackLogger` directory.

## Compilation Commands

To compile standard CSS:
```bash
npx sass wwwroot/css/site2.scss wwwroot/css/site2.css
```

To compile minified/compressed CSS:
```bash
npx sass wwwroot/css/site2.scss wwwroot/css/site2.min.css --style compressed
```

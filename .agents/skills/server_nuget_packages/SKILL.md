---
name: server_nuget_packages
description: Guidelines for managing NuGet package references in the .NET projects of this repository (GnollHackServer.Data, MobileGnollHackLogger, Overseer, Overseer.Tests). Covers where the shared packages are declared, how to trace and fix a NuGet vulnerability warning (NU1901-NU1904) that a transitive dependency causes, and the deliberate direct reference to System.Security.Cryptography.Pkcs that overrides the vulnerable version Microsoft.Data.SqlClient brings in. Read before adding, upgrading, removing or questioning a PackageReference, and whenever a build reports a NuGet audit warning.
---

# NuGet Packages in the .NET Projects

The solution `MobileGnollHackLogger.slnx` holds four .NET projects, all targeting the same
`net10.0` framework: `GnollHackServer.Data` (the EF Core data layer), `MobileGnollHackLogger`
(the Razor Pages site), `Overseer` (the ASP.NET Core API with the Angular client) and
`Overseer.Tests`. There is no `Directory.Packages.props` and no central package management:
each `.csproj` lists its own `PackageReference`s with explicit versions. `GnollHackServer.Data`
is referenced by the other three projects, so a package it references, directly or
transitively, reaches all four.

## Conventions

- **Versions live in the `.csproj` files**, nothing else. Never hardcode a package version in a
  skill, a plan or a document; name the project file instead. The versions quoted in this skill
  are the ones at the time of writing and are given only so the reasoning can be checked.
- **The EF Core and ASP.NET Core packages move together** to the same `10.0.x` patch version in
  every project. Upgrade them as a set.
- **Keep the build at zero warnings.** NuGet audit warnings (`NU1901`–`NU1904`) are build
  warnings this repository does not tolerate, so a vulnerable package is fixed, not suppressed.
  Do not add `NoWarn` or `NuGetAuditLevel` to hide one.

## Tracing a NuGet Audit Warning

A `NU1902` (moderate) or `NU1903` (high) warning names a package and an advisory URL. The
package is usually **not** referenced directly. Find who brings it in, from the repository root:

```bash
dotnet nuget why GnollHackServer.Data/GnollHackServer.Data.csproj <PackageId>
```

Then open the advisory it links (`https://github.com/advisories/GHSA-...`) and read the
*patched versions* per major line. Fix it in this order:

1. **Upgrade the direct package** that pulls it in, if a release exists whose own dependency is
   already a patched version. Check with the package's `.nuspec` on nuget.org
   (`https://api.nuget.org/v3-flatcontainer/<id>/<version>/<id>.nuspec`), looking at the
   dependency group of the project's target framework.
2. **Otherwise add a direct `PackageReference`** to the vulnerable package at a patched version,
   in the project nearest the root of the dependency chain, usually `GnollHackServer.Data`.
   NuGet's nearest-wins rule makes the direct reference override the transitive one, and the
   referencing projects inherit it, so one project file is enough. Put a one-line comment above it
   saying what it overrides and why, and record it under *Deliberate Pins* below so the next
   reader does not "clean it up".
3. **Verify**, from the repository root, that the warning is gone and the version resolved:
   ```bash
   dotnet build MobileGnollHackLogger.slnx
   ```
   ```bash
   dotnet nuget why GnollHackServer.Data/GnollHackServer.Data.csproj <PackageId>
   ```

## Deliberate Pins

Direct references that exist only to override a transitive version. Each one can be removed
again once the package that pulls in the vulnerable version depends on a patched one itself;
check that with step 1 above before removing.

### `System.Security.Cryptography.Pkcs` in `GnollHackServer.Data`

- **Why it exists.** `Microsoft.EntityFrameworkCore.SqlServer` depends on
  `Microsoft.Data.SqlClient`, whose `net9.0` dependency group (the one a `net10.0` project
  resolves) pins `System.Security.Cryptography.Pkcs` to a `9.0.x` version that two .NET
  denial-of-service advisories cover, `GHSA-2fmv-rv2c-59x7` (moderate, CVE-2026-70162) and
  `GHSA-846f-f764-76mx` (high, CVE-2026-84008). Every project in the solution then reported
  `NU1902` and `NU1903` on each build.
- **Why upgrading SqlClient did not fix it** (as of 2026-10-14). The newest
  `Microsoft.Data.SqlClient` release at the time, 7.1.1, still pinned Pkcs 9.0.18, inside the
  vulnerable range (`<= 9.0.20`), so no SqlClient upgrade removed the warning.
- **What was done.** A direct `PackageReference` to `System.Security.Cryptography.Pkcs` at the
  first patched `10.0.x` version was added to `GnollHackServer.Data.csproj`, with a comment
  above it. `Overseer`, `MobileGnollHackLogger` and `Overseer.Tests` inherit it through their
  project reference and needed no change. The build went to zero warnings and
  `dotnet nuget why` shows the `10.0.x` version resolving in every project.
- **When to remove it.** When a `Microsoft.Data.SqlClient` release whose `net9.0` (or a newer
  `net10.0`) dependency group names a patched Pkcs version is in use. Remove the reference, then
  rebuild the solution and confirm the warning does not return. Until then, keep the pinned
  version at or above the advisory's first patched version of its major line, and move it along
  with the framework patch version when the `10.0.x` set is upgraded.

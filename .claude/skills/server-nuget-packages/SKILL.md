---
name: server-nuget-packages
description: Guidelines for managing NuGet package references in the .NET projects of this repository (GnollHackServer.Data, MobileGnollHackLogger, Overseer, Overseer.Tests). Covers where the shared packages are declared, how to trace and fix a NuGet vulnerability warning (NU1901-NU1904) that a transitive dependency causes, and the deliberate direct reference to System.Security.Cryptography.Pkcs that overrides the vulnerable version Microsoft.Data.SqlClient brings in. Read before adding, upgrading, removing or questioning a PackageReference, and whenever a build reports a NuGet audit warning.
---

The full skill lives in this repository's tool-neutral agent directory (`.agents/`),
which is shared with other AI coding agents. This file is only a pointer.

Read `.agents/skills/server_nuget_packages/SKILL.md` (path relative to the repository root) in full
before proceeding, and follow it. Any `references/` files it links are relative to that
same directory.
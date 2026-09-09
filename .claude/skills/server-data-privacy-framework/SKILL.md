---
name: server-data-privacy-framework
description: >-
  Map and traps for Overseer's data privacy framework — the layer that decides what leaves the
  server, what is stored in clear and what is not. Read before touching confidential sessions,
  ephemeral (incognito) sessions, envelope encryption or crypto-shredding, outbound DLP masking,
  attachment validation or malware scanning, document parsing and RAG, the untrusted-content
  boundary, the provider confidentiality posture ladder, the custom-endpoint SSRF guard, the
  security headers and CSP, or the telemetry suppression that hangs off all of them. Answers
  "why is this decrypted into a local and that one in place", "where is content actually
  encrypted", "why does the validator classify three ways", "why does a floor parse its own
  booleans", "why is a session reference a type", "what does an ephemeral session refuse to
  write", and "which claim am I about to overstate". The authoritative description of what
  exists is docs/overseer/data-privacy-framework.md; this skill is the orientation map, the
  mistakes already made and fixed, and the honesty rules a change must not quietly lower.
---

The full skill lives in this repository's tool-neutral agent directory (`.agents/`),
which is shared with other AI coding agents. This file is only a pointer.

Read `.agents/skills/server_data_privacy_framework/SKILL.md` (path relative to the repository root) in full
before proceeding, and follow it. Any `references/` files it links are relative to that
same directory.
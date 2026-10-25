---
name: sentry-logging-architecture
description: Documentation of the Sentry crash logging architecture for the Overseer project, including the tunnel endpoint, authentication filtering, AI provider error suppression, and source map exclusion logic. Also covers the website's (MobileGnollHackLogger) backend-only Sentry setup and its WebsiteSentryEventProcessor.
---

The full skill lives in this repository's tool-neutral agent directory (`.agents/`),
which is shared with other AI coding agents. This file is only a pointer.

Read `.agents/skills/sentry_logging_architecture/SKILL.md` (path relative to the repository root) in full
before proceeding, and follow it. Any `references/` files it links are relative to that
same directory.
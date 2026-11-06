# Linux Desktop Implementation Plan

> For agentic workers: execute sequentially, using test-driven-development and verification-before-completion.

**Goal:** Deliver a native Ubuntu/Linux Mint x86-64 build without changing Windows delivery or real data.
**Architecture:** A Linux host reuses Desktop/Core. Platform boundaries cover data paths, shell actions and secure sessions.
**Tech Stack:** .NET 10, Avalonia 12, Linux Secret Service.
**Spec:** docs/superpowers/specs/2026-11-06-linux-desktop-design.md

- [x] Add portable tests: Linux defaults are absolute Linux paths; settings remain outside application; secure-session roundtrip/no plaintext and failure reporting.
- [x] Run tests red, implement ApplicationPaths and Linux host/session bridge, rerun green.
- [x] Replace explorer-only folder action with cross-platform shell action; retain Windows selection.
- [x] Publish self-contained linux-x64, package tar.gz with instructions; add repeatable publish script and solution entries.
- [x] Verify in Ubuntu with synthetic data and graphical startup, record exact limitations; run affected Windows regression tests.

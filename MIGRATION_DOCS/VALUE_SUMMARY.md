# Value Summary

<!-- [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md -->

AgOpenGPS — the open-source precision-agriculture guidance and auto-steer software — now runs natively on Windows, macOS, and Linux, instead of Windows only.

## What changed under the hood

The software has been rebuilt on a modern, cross-platform foundation (modern .NET together with the Avalonia user-interface toolkit), so it is no longer tied to Windows-only technology. In everyday terms, this future-proofs the project and gives users a much wider choice of computers and hardware to run it on.

## What did not change

This is a transition, not a redesign. The screens, the on-screen controls, the way guidance and auto-steer behave, and all of your saved field and settings data are preserved — existing users will find the very same product they already know. The familiar two-program setup is unchanged as well: the main guidance program (AgOpenGPS) and its input/output helper (AgIO), which connects to your GPS and steering hardware, still work together exactly as before, talking to each other over the same private internal connection.

## Why it matters

Farmers and the wider community can now run AgOpenGPS on inexpensive Linux machines, on Macs, and on Windows alike — broadening who can use it and on what equipment. Just as importantly, the modern foundation makes the software easier to maintain and support for years to come, all without giving up any existing capability.

## A note on optional extras

A few optional, hardware-specific conveniences — such as screen-brightness control on some systems, the webcam view, and online background map imagery — may be limited or switched off on certain operating systems where there is no equivalent. These extras never affect core guidance, section control, or your ability to start and run the program.

## How we know it still works

To make sure nothing changed in how the software behaves, the team's work is being verified by automated "golden-file" comparison tests that run on all three operating systems and check the new versions against trusted reference results from the original.

---

For technical detail, see CHANGELOG.md, TRANSITION_MAP.md, FEATURE_TRACEABILITY.md, and PARITY_REPORT.md in this folder.

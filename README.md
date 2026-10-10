<<<<<<< HEAD
# rust-animal-plugin
=======
# Beastmaster (working title) - Rust plugin

**Status: PREPARED - waiting for the START command. No plugin code has been written yet.**
Last updated: 2026-10-09 - target game build: Rust 25823813 (Livestock Update, Oct 2026).

Ride, tame, command and *become* animals on a Rust server.
Deliverable: one C# file `Beastmaster.cs` (Oxide/Carbon plugin) that the owner uploads to the server at wespner.eu.

## Rules set by the owner
1. Everything the player sees is **English** - menu, help, chat, UI, config comments.
2. The **F5 menu** has an in-game **Help/Guide that explains everything**.
3. Menu access is managed by the **admin with commands**; menu access also allows **spawning animals**.
4. The plugin is **not written until the owner says START**. Until then: study and preparation only.
5. The owner expects everything in the brief to work without errors. The process that earns that is in [docs/PLAN.md](docs/PLAN.md) (compile against the real server DLLs, spike every engine-dependent mechanism first, test on a local dev server, written in-game acceptance checklist).

## Documents
| File | What it is |
|---|---|
| [docs/SPEC.md](docs/SPEC.md) | What to build: every requirement from the brief, numbered, with risk tags and the honest literal-vs-equivalent list |
| [docs/RESEARCH.md](docs/RESEARCH.md) | What was verified against the real game/server (and what was not), claims check of the community notes, engine limits |
| [docs/PLAN.md](docs/PLAN.md) | How and in which order: dev environment, spikes S0-S17, phases, test strategy, deployment |
| [docs/_archive/brief-original-cs.md](docs/_archive/brief-original-cs.md) | The owner's brief, verbatim (Czech) + later additions |

## Dev environment (already prepared on this PC)
| Path | Content |
|---|---|
| `C:\RustDev\server` | Rust Dedicated Server, build 25823813 (vanilla, nothing installed into it yet) |
| `C:\RustDev\orig-managed` | pristine copy of the server's managed DLLs |
| `C:\RustDev\oxide`, `C:\RustDev\carbon` | Oxide 2.0.7820 and Carbon (production build) release files, kept separate from the server |
| `C:\RustDev\tools\ilspy` | ILSpy command line 9.1 (decompiler) |
| `C:\RustDev\decompiled\orig` | decompiled game code (greppable C#) |
| `C:\RustDev\decompiled\oxide-patched` | decompiled Oxide-patched game code = the real hook list |
| `C:\RustDev\logs` | download/decompile logs |

Everything under `C:\RustDev` can be deleted at any time without touching the project.

## Folder layout planned for START
```
Rust Animal Plugin/
  docs/           these documents
  src/            plugin source modules (C#), build script, tests
  dist/           Beastmaster.cs  <- the single file to upload, + INSTALL.md
  tests/          acceptance checklist, smoke-test scripts
```
>>>>>>> cc23e25 (Init commit)

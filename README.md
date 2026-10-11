# Beastmaster - Rust plugin

Ride, tame, command and *become* animals on a Rust server (Oxide / Carbon).
Deliverable: one C# file, `dist/Beastmaster.cs`, uploaded to the server at wespner.eu.

**Status:** version 1.2.4 (2026-10-11) - the owner's test of 1.1.0 is worked through: the horse's inventory on every animal, saddles
on the back, stamina, seats fixed to the spine, camera-true attacks and more. The changes, what is uncertain and what to test are in
[docs/AUDIT.md](docs/AUDIT.md). Install: [INSTALL.md](INSTALL.md).

## Rules set by the owner
1. Everything the player sees is **English** - menu, help, chat, UI, config comments.
2. The **menu** (Ctrl + R, `/bm`, or F5 after a one-time bind) has an in-game **Help/Guide that explains everything**.
3. Menu access is managed by the **admin with commands**; menu access also allows **spawning animals**.
4. The owner tests the plugin on his own server and reports what works and what does not.

## Documents
| File | What it is |
|---|---|
| [INSTALL.md](INSTALL.md) | Installing, permissions, commands, updating |
| [docs/AUDIT.md](docs/AUDIT.md) | The changes of 1.2.0 and the audit of 1.1.0 (Czech): what was fixed, what is uncertain, what to test |
| [docs/STATUS.md](docs/STATUS.md) | Build status against the specification |
| [docs/SPEC.md](docs/SPEC.md) | What to build: every requirement from the brief, numbered |
| [docs/RESEARCH.md](docs/RESEARCH.md) | What was verified against the real game/server, engine limits |
| [docs/PLAN.md](docs/PLAN.md) | How it was built: dev environment, spikes, phases, tests |
| [docs/_archive/brief-original-cs.md](docs/_archive/brief-original-cs.md) | The owner's brief, verbatim (Czech) |

## Building
`src/*.cs` are the modules; `tools/bundle.py` joins them into `dist/Beastmaster.cs`; `tools/build.ps1` bundles and compiles the result
against the real server DLLs of the local dev server (`build/Harness.csproj`).

```
powershell -File tools/build.ps1            # bundle + compile check
powershell -File tools/build.ps1 -Deploy    # and copy to the local dev server
```

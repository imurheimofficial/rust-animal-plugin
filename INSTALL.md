# Beastmaster - installation

One file: `dist/Beastmaster.cs` (generated from `src/*.cs`; ~12 000 lines). Copy it, nothing else is needed.

## Requirements

| | |
|---|---|
| Game | Rust, the build it was written and tested against: 25823813 (protocol 2634) |
| Framework | **Oxide / uMod** 2.0.7820 (tested). **Carbon** uses the same plugin API and should work; it was not tested. |
| Other plugins | none required. |

## Install

1. Stop nothing: Oxide and Carbon load plugins while the server runs.
2. Upload `Beastmaster.cs` to
   * Oxide: `<server>/oxide/plugins/`
   * Carbon: `<server>/carbon/plugins/`
3. Watch the console: `Beastmaster 1.0.0 ready: 20 species, 13 modules, ON`.
4. Run `bm.selftest` in the server console (or RCON). It lists every species prefab, item, effect and seat the plugin needs and tells what is missing in your build.
5. The first start writes
   * `oxide/config/Beastmaster.json` - every number of the specification (carbon: `carbon/configs/`),
   * `oxide/data/Beastmaster.json` - owned animals, horse temperaments, bosses, settings of players,
   * `oxide/lang/en/Beastmaster.json` - every text the players see (edit freely).

## Permissions

Server owners (auth level 2) and the people with `beastmaster.admin` can do everything. Everybody else needs:

| Permission | What it allows |
|---|---|
| `beastmaster.use` | open the menu, own animals, commands, gear |
| `beastmaster.ride` | ride the standard species |
| `beastmaster.ride.restricted` | ride chicken, crocodile, snake, rabbit, squirrel, frog (admin only by default) |
| `beastmaster.tame` | tame animals |
| `beastmaster.transform` | the ritual of transformation |
| `beastmaster.transform.restricted` | the ritual for the restricted species |
| `beastmaster.spray` | re-roll the look of an animal with the spray can |
| `beastmaster.menu` | control layer of the menu (Admin, Spawn, Players) and spawning animals |
| `beastmaster.admin` | everything |

Typical setup (Oxide; Carbon uses `c.grant` / `c.group`):

```
oxide.grant group default beastmaster.use
oxide.grant group default beastmaster.ride
oxide.grant group default beastmaster.tame
oxide.grant group default beastmaster.spray
oxide.grant group default beastmaster.transform
oxide.grant user <steamid> beastmaster.menu
```

Or from the plugin itself: `bm.grant <player|steamid>` gives menu access (and `use`), `bm.grantgroup <group>` does the same for a group.

## Commands

| | |
|---|---|
| Chat | `/bm` (menu), `/bm help`, `/bm species`, `/bm keys`, `/bm view`, `/bm preset a|b|c|d`, `/bm revert`, `/bm cmd follow|wait|guard|attack|release`, `/bm ride`, `/bm form` |
| Console (players) | `bm.menu`, `bm.view`, `bm.preset`, `bm.revert`, `bm.cmd`, ... - the F5 key cannot be bound by a server; a player may type `bind f5 "bm.menu"` once in the F1 console |
| Console (admin) | `bm.enable`, `bm.disable`, `bm.grant`, `bm.revoke`, `bm.access`, `bm.grantgroup`, `bm.revokegroup`, `bm.spawn <species> [count] [tamed|wild] [saddled] [near] [player]`, `bm.clear [radius]`, `bm.reload`, `bm.selftest`, `bm.debug <0-3>`, `bm.horses list|wild|set|sleep`, `bm.boss spawn|clear`, `bm.prefabs <words>`, `bm.items <words>`, `bm.test ...` |

## Things a server owner should know

* **Wild horses are born without a saddle** (`Horse.WildUnsaddled`). Only horses that spawn *after* the plugin is loaded are changed; the horses that exist, belong to players, are for sale at a vendor or were made by other plugins are never touched. `bm.horses wild` turns the idle horses that exist now into wild ones (it does not touch ridden horses, owned ones or those of vendors).
* **Bosses** (`Features.BossAnimals`, on by default) appear at night with 4 % chance every 5 minutes, near a random player, with 4000 health and 99 damage per hit, and set victims on fire. Switch the feature off in the menu (Admin) or the config if you do not want that. `bm.boss spawn bull` makes one at once for testing.
* **Climate** (`Features.Climate`): a polar bear outside the snow needs water, other animals in the snow need cooked meat in the inventory, otherwise they are slower and weaker. Applies to ridden animals and animal forms only.
* **Animal forms** keep the human's inventory; the human body is hidden and cannot be hurt, the animal's health is the player's health (the animal dies - the player dies). Needs (hunger, thirst, stamina) are gentle: nothing starves unless `Animal form.StarveDamage` is on.
* The plugin raises the game's livestock lead-rope limit (`livestock.leadbreakdistance`) from 7 m to 50 m while it is loaded and puts the old value back when it unloads.
* Unload and reload are safe: riders and animal forms are returned to the normal state, the data stays.
* All text is in `oxide/lang/en/Beastmaster.json`; all numbers in the config; the help in the menu is generated from the live config.

## Update

Replace the file. New config keys are added with their defaults, old ones are kept. A broken config file is reported in the console and the defaults are used for that start (fix or delete the file).

## Uninstall

Delete the file (or `o.unload Beastmaster`). The data file keeps the owned animals; animals that were parked by the plugin are released on unload. Wild horses get the saddle of the game back when the plugin unloads (and lose it again when it loads), so nothing stays broken without the plugin. Horses that were tamed keep the saddle item in their equipment slot.

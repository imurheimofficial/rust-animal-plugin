using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Oxide.Game.Rust.Cui;
using Rust.Ai.Gen2;
using UnityEngine;

namespace Oxide.Plugins
{
    public partial class Beastmaster
    {
        // ---------------------------------------------------------------- the menu (R12)
        // A window of the game's own UI with two layers: the player layer (Home, Help, My animals, Controls, Transformation) for everybody
        // with beastmaster.use and the control layer (Admin, Spawn, Players) for owners, moderators and everyone granted beastmaster.menu.
        // The server cannot bind F5 on a client, so the menu opens with /bm or the console command bm.menu (bind f5 "bm.menu" in F1 works).

        private const string MenuRoot = "BeastmasterMenu";
        private static readonly Regex TagRegex = new Regex("<[^>]+>", RegexOptions.Compiled);

        internal sealed class MenuState
        {
            public bool Open;
            public string Page = "home";
            public string Topic = "overview";
            public string Species = "cow";            // the species card of the Help topic and the Spawn page
            public string Note = string.Empty;        // what the player looked at ("a shy horse")
            public string Search = string.Empty;
            public int TextPage;
            public int ListPage;
            public ulong ConfirmRelease;
            public int SpawnCount = 1;
            public bool SpawnTamed;
            public bool SpawnSaddled;
            public bool SpawnMini;
            public ulong RenameTag;
        }

        internal readonly Dictionary<ulong, MenuState> MenuStates = new Dictionary<ulong, MenuState>();

        private MenuState MenuOf(BasePlayer player)
        {
            MenuState st;
            if (!MenuStates.TryGetValue(player.userID, out st))
            {
                st = new MenuState();
                MenuStates[player.userID] = st;
            }
            return st;
        }

        private static readonly string[][] MenuPages =
        {
            new[] { "home", "Home", "player" },
            new[] { "help", "Help / Guide", "player" },
            new[] { "animals", "My animals", "player" },
            new[] { "controls", "Controls", "player" },
            new[] { "ritual", "Transformation", "player" },
            new[] { "admin", "Admin", "control" },
            new[] { "spawn", "Spawn", "control" },
            new[] { "tools", "Aim tools", "control" },
            new[] { "players", "Players", "control" },
        };

        private static bool IsControlPage(string page)
        {
            return MenuPages.Any(p => p[0] == page && p[2] == "control");
        }

        internal void OpenMenu(BasePlayer player, string page)
        {
            if (player == null || !player.IsConnected)
            {
                return;
            }
            if (!Has(player, PermUse) || (!Cfg.Access.OpenForEveryone && !HasMenuAccess(player)))
            {
                Tell(player, "NoPermission");
                return;
            }
            MenuState st = MenuOf(player);
            if (st.Open && (string.IsNullOrEmpty(page) || page == "home"))
            {
                CloseMenu(player);                  // the menu command toggles
                return;
            }
            st.Open = true;
            if (!string.IsNullOrEmpty(page) && MenuPages.Any(p => p[0] == page) && (!IsControlPage(page) || HasMenuAccess(player)))
            {
                st.Page = page;
            }
            DrawMenu(player);
        }

        internal void CloseMenu(BasePlayer player)
        {
            if (player == null)
            {
                return;
            }
            MenuState st;
            if (MenuStates.TryGetValue(player.userID, out st))
            {
                st.Open = false;
            }
            if (player.IsConnected)
            {
                CuiHelper.DestroyUi(player, MenuRoot);
            }
        }

        internal void CloseAllMenus()
        {
            foreach (BasePlayer p in BasePlayer.activePlayerList.ToList())
            {
                MenuState st;
                if (p != null && MenuStates.TryGetValue(p.userID, out st) && st.Open)
                {
                    CloseMenu(p);
                }
            }
        }

        // ---------------------------------------------------------------- small UI helpers

        private static string Num(float v)
        {
            return v.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private static string Pt(float x, float y)
        {
            return Num(x) + " " + Num(y);
        }

        private static string OnOff(bool on)
        {
            return on ? "<color=#8fd36b>on</color>" : "<color=#d36b6b>off</color>";
        }

        private static void UiPanel(CuiElementContainer c, string parent, string name, float x0, float y0, float x1, float y1, string color, bool cursor = false)
        {
            c.Add(new CuiPanel { Image = { Color = color }, RectTransform = { AnchorMin = Pt(x0, y0), AnchorMax = Pt(x1, y1) }, CursorEnabled = cursor }, parent, name);
        }

        private static void UiLabel(CuiElementContainer c, string parent, string text, int size, float x0, float y0, float x1, float y1, TextAnchor align = TextAnchor.UpperLeft, string color = "0.92 0.93 0.88 1")
        {
            c.Add(new CuiLabel { Text = { Text = text, FontSize = size, Align = align, Color = color }, RectTransform = { AnchorMin = Pt(x0, y0), AnchorMax = Pt(x1, y1) } }, parent);
        }

        private static void UiButton(CuiElementContainer c, string parent, string text, string command, float x0, float y0, float x1, float y1, string color, int size = 13)
        {
            c.Add(new CuiButton
            {
                Button = { Command = command, Color = color },
                RectTransform = { AnchorMin = Pt(x0, y0), AnchorMax = Pt(x1, y1) },
                Text = { Text = text, FontSize = size, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, parent);
        }

        private const string ColNav = "0.20 0.24 0.16 1";
        private const string ColNavOn = "0.36 0.50 0.22 1";
        private const string ColBtn = "0.24 0.30 0.18 1";
        private const string ColOn = "0.30 0.48 0.20 1";
        private const string ColOff = "0.50 0.20 0.17 1";
        private const string ColWarn = "0.55 0.38 0.12 1";

        // ---------------------------------------------------------------- the window

        internal void DrawMenu(BasePlayer player)
        {
            if (player == null || !player.IsConnected)
            {
                return;
            }
            MenuState st = MenuOf(player);
            CuiElementContainer c = BuildMenu(player, st, HasMenuAccess(player));
            CuiHelper.DestroyUi(player, MenuRoot);
            CuiHelper.AddUi(player, c);
        }

        // The whole window as a container (kept apart from sending it so that it can be tested without a connected player).
        internal CuiElementContainer BuildMenu(BasePlayer player, MenuState st, bool control)
        {
            if (!control && IsControlPage(st.Page))
            {
                st.Page = "home";
            }
            var c = new CuiElementContainer();
            UiPanel(c, "Overlay", MenuRoot, 0.17f, 0.10f, 0.83f, 0.90f, "0.06 0.07 0.05 0.96", true);
            UiLabel(c, MenuRoot, "<b>Beastmaster</b>  <size=12><color=#9aa58f>" + Version + "</color></size>", 21, 0.02f, 0.925f, 0.7f, 0.995f, TextAnchor.MiddleLeft);
            UiButton(c, MenuRoot, "X", "bm.ui close", 0.95f, 0.935f, 0.99f, 0.99f, "0.55 0.15 0.12 1", 14);
            if (!Active)
            {
                // switched off: only the switch is offered
                UiLabel(c, MenuRoot, "<size=20><b>Beastmaster is OFF</b></size>\n\nNothing of it works while it is off: no riding, no taming, no animal forms, no commands.", 15, 0.05f, 0.45f, 0.95f, 0.85f, TextAnchor.UpperLeft);
                if (control)
                {
                    UiButton(c, MenuRoot, "Enable Beastmaster", "bm.ui plugin", 0.30f, 0.25f, 0.70f, 0.38f, ColOn, 20);
                }
                else
                {
                    UiLabel(c, MenuRoot, "<color=#d9b85a>Ask an admin to enable it (they need the permission beastmaster.menu).</color>", 15, 0.05f, 0.25f, 0.95f, 0.38f, TextAnchor.MiddleCenter);
                }
                return c;
            }
            float y = 0.86f;
            bool controlLabel = false;
            foreach (string[] p in MenuPages)
            {
                if (p[2] == "control")
                {
                    if (!control)
                    {
                        continue;
                    }
                    if (!controlLabel)
                    {
                        controlLabel = true;
                        UiLabel(c, MenuRoot, "<size=11><color=#9aa58f>CONTROL LAYER</color></size>", 11, 0.015f, y - 0.03f, 0.2f, y, TextAnchor.LowerLeft);
                        y -= 0.04f;
                    }
                }
                UiButton(c, MenuRoot, p[1], "bm.ui page " + p[0], 0.015f, y - 0.06f, 0.2f, y, st.Page == p[0] ? ColNavOn : ColNav, 14);
                y -= 0.07f;
            }
            string content = MenuRoot + ".content";
            UiPanel(c, MenuRoot, content, 0.215f, 0.025f, 0.99f, 0.915f, "0.11 0.12 0.09 0.92");
            switch (st.Page)
            {
                case "help": DrawHelp(c, content, player, st); break;
                case "animals": DrawAnimals(c, content, player, st); break;
                case "controls": DrawControls(c, content, player, st); break;
                case "ritual": DrawRitual(c, content, player, st); break;
                case "admin": DrawAdmin(c, content, player, st); break;
                case "spawn": DrawSpawn(c, content, player, st); break;
                case "tools": DrawTools(c, content, player, st); break;
                case "players": DrawPlayers(c, content, player, st); break;
                default: DrawHome(c, content, player, st); break;
            }
            return c;
        }

        private string FeatureLine()
        {
            var sb = new StringBuilder();
            foreach (FieldInfo f in typeof(FeatureCfg).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.FieldType == typeof(bool))
                {
                    sb.Append(Humanise(f.Name)).Append(' ').Append(OnOff((bool)f.GetValue(Cfg.Features))).Append("   ");
                }
            }
            return sb.ToString();
        }

        private static string Humanise(string name)
        {
            return Regex.Replace(name, "(?<=[a-z])(?=[A-Z])", " ").ToLowerInvariant();
        }

        // ---------------------------------------------------------------- Home

        private void DrawHome(CuiElementContainer c, string parent, BasePlayer player, MenuState st)
        {
            Driver d = DriverOf(player);
            var sb = new StringBuilder();
            sb.Append("<size=20><b>Welcome, ").Append(player.displayName).Append("</b></size>\n\n");
            sb.Append("Beastmaster is ").Append(OnOff(Active)).Append(".\n");
            sb.Append(d == null ? "You are on foot." : d.Kind == ControlKind.Ride ? "You are riding a " + d.Species.Display + "." : "You are in the form of a " + d.Species.Display + ".").Append('\n');
            sb.Append("Your animals: ").Append(OwnedCount(player.userID)).Append(" of ").Append(Cfg.Taming.MaxOwned).Append(".\n");
            sb.Append("Menu layer: ").Append(HasMenuAccess(player) ? "player + control (Admin, Spawn, Aim tools, Players)" : "player").Append("   |   open it with Ctrl + R or /bm\n\n");
            sb.Append("<b>Features on this server</b>\n").Append(FeatureLine()).Append("\n\n");
            sb.Append("<b>Where to start</b>\n");
            sb.Append("Help / Guide explains everything, generated from this server's settings.\n");
            sb.Append("My animals lists your tamed animals and lets you command them.\n");
            sb.Append("Controls holds your own camera and view settings.\n");
            sb.Append("Transformation tells how to become an animal and shows your progress.\n\n");
            sb.Append("<color=#9aa58f>Aim at an animal and press the button below to get its species card.</color>");
            UiLabel(c, parent, sb.ToString(), 14, 0.03f, 0.2f, 0.97f, 0.97f);
            if (HasMenuAccess(player))
            {
                UiButton(c, parent, "Disable Beastmaster", "bm.ui plugin", 0.66f, 0.90f, 0.97f, 0.98f, ColOff, 15);
            }
            UiButton(c, parent, "Help for what I'm looking at", "bm.ui lookhelp", 0.03f, 0.06f, 0.38f, 0.15f, ColBtn);
            UiButton(c, parent, "Open the Help", "bm.ui page help", 0.40f, 0.06f, 0.62f, 0.15f, ColBtn);
            if (d != null)
            {
                UiButton(c, parent, d.Kind == ControlKind.Ride ? "Get off" : "Back to human", "bm.ui revert", 0.64f, 0.06f, 0.88f, 0.15f, ColWarn);
            }
        }

        // ---------------------------------------------------------------- Help

        internal sealed class HelpTopic
        {
            public string Id;
            public string Title;
            public Func<BasePlayer, string> Text;
        }

        private List<HelpTopic> HelpTopics()
        {
            var list = new List<HelpTopic>();
            Action<string, string, Func<BasePlayer, string>> add = (id, title, text) => list.Add(new HelpTopic { Id = id, Title = title, Text = text });
            add("overview", "Overview", HelpOverview);
            add("controls", "Controls (all keys)", HelpControls);
            add("riding", "Riding", HelpRiding);
            add("gear", "Gear and horseshoes", HelpGear);
            add("taming", "Taming", HelpTaming);
            add("commands", "Commanding animals", HelpCommands);
            add("combat", "Combat and rear attacks", HelpCombat);
            add("ritual", "Transformation ritual", HelpRitual);
            add("camera", "Third person", HelpCamera);
            add("nightvision", "Night vision", p => "<b>Night vision</b>\n\nThe game's own Night Vision Goggles give the vision. Carry a pair in your inventory: as an animal they go onto your hidden human head by themselves at night and back into the inventory by day. The Night button on the Controls page (and H after a one-time bind h bm.nv in the F1 console) switches them by hand.\n\nWithout goggles in the inventory nothing happens. The goggles need the head slot: take your helmet or hat off first.");
            add("spray", "Spray can", HelpSpray);
            add("relations", "Animal relations", HelpRelations);
            add("species", "Species cards", p =>
            {
                MenuState st = MenuOf(p);
                SpeciesDef sp = SpeciesById(st.Species) ?? SpeciesById("cow");
                return (string.IsNullOrEmpty(st.Note) ? string.Empty : "<color=#e6b84a>" + st.Note + "</color>\n\n") + SpeciesCard(sp);
            });
            add("permissions", "Permissions and commands", HelpPermissions);
            add("faq", "FAQ / troubleshooting", HelpFaq);
            return list;
        }

        private string HelpOverview(BasePlayer p)
        {
            return "<b>Beastmaster " + Version + "</b> - animals as mounts, companions and forms.\n\n"
                + "Tame animals, fit saddles and gear, command them and ride them. With the ritual of transformation you can become an animal yourself.\n\n"
                + "<b>Quick start</b>\n"
                + "1. Tame an animal (Help - Taming).\n"
                + "2. Press E on it: without a saddle its inventory opens. Put a saddle into slot 1 and close the panel.\n"
                + "3. Press E again to mount (hold E for all choices). W/A/S/D move, Shift faster, F attack, R switches first / third person, G jumps, Space gets you off.\n"
                + "4. Command it: /bm cmd follow | wait | guard | attack | release.\n\n"
                + "<b>Switched on here</b>\n" + FeatureLine() + "\n\n"
                + "Open this menu any time with /bm.";
        }

        private string HelpControls(BasePlayer p)
        {
            return "<b>Keys as an animal (third person) and while riding</b>\n"
                + "W/A/S/D  move\n"
                + "Shift  go faster - each press climbs one gait: walk, jog, run, sprint, full sprint. W alone walks; after a stop it walks again (the horse: walk, trot, canter, gallop, full gallop)\n"
                + "S  brake; S with A or D while running: turn around 180 degrees (a slide for bulls and rams)\n"
                + "G  jump - over fences and onto raised surfaces: floors, roofs, crates, rocks (cooldown " + Cfg.Movement.JumpCooldown + " s)\n"
                + "Space  as an animal: back to human (the animal stays) - while riding: get off\n"
                + "F  attack - camera to the front: the attack to the front; camera behind the animal: the rear kick (cow, bull, sheep, ram, stag, horse)\n"
                + "E  as an animal: eat food on the ground, drink fresh water, graze, pick things up into the saddle bag, open doors and containers, lead your tamed animals (the prompt on the screen says what E does)\n"
                + "Ctrl + E  as an animal: your animal's inventory (saddle, horseshoes, saddle bag, horse armour)\n"
                + "E at an animal (as a human): ride it when it has a saddle, its inventory when it has none. Ctrl + E: its inventory (a player's animal: the leash). Hold E: all choices\n"
                + "R  as an animal: another skin - while riding: first / third person. Hold R: the skin window (colours, spots, breeds)\n"
                + "Night vision  comes by itself at night when you carry night vision goggles (they need the head slot)\n"
                + "B  gesture wheel: lie down, sit, roar, look around - /bm lie, sit, roar, look, stand do the same\n"
                + "X  form: back to human. X at the animal you have left: become it again. E at it: ride it (with a saddle). Ctrl + E at it: its inventory\n"
                + "Stamina  running tires the animal like a horse (the yellow bar above the game's bars); out of breath it only trots and cannot jump until it has rested\n"
                + "Ctrl + R  this menu (empty hands, riding or as an animal), also /bm\n\n"
                + "<color=#9aa58f>F5 and H need a one-time bind in the F1 console: bind f5 bm.menu and bind h bm.nv (the game keeps them).</color>";
        }

        private string HelpRiding(BasePlayer p)
        {
            return "<b>Riding</b> " + OnOff(Cfg.Features.Riding) + "\n\n"
                + "Who: " + (Cfg.Riding.RequireTame ? "tamed animals" : "any animal") + (Cfg.Riding.RequireSaddle ? " with a saddle" : string.Empty) + ". Owner may ride; " + OwnershipText() + ".\n"
                + "How: look at the animal within " + Cfg.Riding.MountDistance + " m and press E. You need a saddle in your own inventory (it is not used up).\n"
                + "Only the horse, the bull and the cow carry two riders; every other animal carries one. A second rider needs a saddle in his own inventory too, unless the first rider carries a double saddle. You sit on its back like on a horse and look to the sides at most.\n"
                + "A player who is an animal is ridden the same way (the horse and the bull and cow two riders, the rest one).\n"
                + "Stamina: like the horse, a sprint lasts a short while; walking and standing give the breath back. A crash into a wall or a fall throws the rider off.\n"
                + "Chicken, crocodile, snake, rabbit, squirrel and frog are restricted species (permission beastmaster.ride.restricted).\n\n"
                + "<b>Good to know</b>\n"
                + "Deep water slows animals like the horse (" + Mathf.RoundToInt(Cfg.Movement.WaterMinSpeed * 100f) + " % at the deepest wading); bears and crocodiles are exempt.\n"
                + "Slopes steeper than " + Cfg.Movement.SlopeLimit + " degrees can make a running animal fall.\n"
                + "Camera: see the Third person topic. The animal's own sounds and animations are played for everything it does.";
        }

        private string OwnershipText()
        {
            switch ((Cfg.Riding.Ownership ?? "team").ToLowerInvariant())
            {
                case "owner": return "nobody else";
                case "anyone": return "anyone else";
                case "friends": return "friends too";
                case "clan": return "clan members too";
                default: return "team members too";
            }
        }

        private string HelpGear(BasePlayer p)
        {
            return "<b>Saddles</b>\n\n"
                + "Animals have no inventory of their own (the horse keeps the game's). The rider carries the saddle in his own inventory: a single saddle seats him, a double saddle also seats a second rider without a saddle of his own. As an animal you keep your own inventory; what you pick up goes into it.\n\n"
                + "<color=#9aa58f>The old inventory (switched off):</color>\n"
                + "slot 1  saddle - single, or double for horse, bull, cow, ram, sheep and stag (the saddle is seen on the back)\n"
                + "slot 2  horseshoes - basic +" + Cfg.Movement.ShoesBasic + " m/s, high quality +" + Cfg.Movement.ShoesAdvanced + " m/s\n"
                + "slot 3  saddle bag - " + Cfg.Gear.BagSlots + " storage slots, " + Cfg.Movement.SaddleBag + " m/s\n"
                + "slot 4  horse armour - wooden: " + Mathf.RoundToInt(Cfg.Gear.ArmorWoodProtection * 100f) + " % less damage, +" + Cfg.Gear.WoodArmorSlots + " slots, " + Cfg.Movement.ArmorWood + " m/s; roadsign: " + Mathf.RoundToInt(Cfg.Gear.ArmorRoadsignProtection * 100f) + " % less damage, " + Cfg.Movement.ArmorRoadsign + " m/s (only the horse shows its armour)\n"
                + "from slot 5  the storage the saddle bag (and the wooden armour) give\n"
                + "Close the panel to save. What an animal form picks up goes into its saddle bag; without one it cannot carry anything.\n\n"
                + (Cfg.Gear.BagPrivate ? "Only the owner and the team can open the inventory of a tamed animal.\n" : "Anyone can open the inventory of a tamed animal.\n")
                + "The inventory stays with the animal (also with the animal you have left as a player) and survives restarts; a dying animal drops it.";
        }

        private string HelpTaming(BasePlayer p)
        {
            return "<b>Taming</b> " + OnOff(Cfg.Features.Taming) + "   (you can own " + Cfg.Taming.MaxOwned + " animals)\n\n"
                + "<b>Bond - horse</b>\nStand within 6 m of a calm wild horse for " + Cfg.Horse.BondSeconds + " s: it is yours, no key needed (the screen shows how far it is). A shy horse runs away and an aggressive one attacks: put it to sleep with an Incapacitate Dart first, it wakes up calm.\n\n"
                + "<b>Bond - cow, bull, sheep, ram</b>\nStay near it and be gentle: it needs " + Cfg.Taming.BondSeconds + " s of familiarity. Then press E on it to claim it. It trusts someone else more? Wait until the other player leaves.\n\n"
                + "<b>Torch - wolf</b>\nChase the wolf with a lit torch for " + Cfg.Taming.WolfChaseSeconds + " s (within " + Cfg.Taming.TorchRange + " m). It stops fleeing, howls and follows you.\n\n"
                + "<b>Predators - bears, boar, panther, tiger</b>\nPhase 1: hit it with an Incapacitate Dart from a Blowpipe - slowed by " + Mathf.RoundToInt(Cfg.Taming.DartSlow * 100f) + " % for " + Cfg.Taming.DartSlowSeconds + " s.\n"
                + "Phase 2: let it run into a Bear Trap (" + Cfg.Taming.TrapDamage + " damage, it is held).\nPhase 3: use " + Cfg.Taming.SyringesNeeded + " Medical Syringes on it (E). It calms down, steps out and follows you after " + Cfg.Taming.NeutralSeconds + " s.\n\n"
                + "<b>Deer - stag</b>\nIncapacitate Dart: it sleeps " + Cfg.Taming.SleepSeconds + " s. Press E, put " + Cfg.Taming.FoodNeeded + " " + Cfg.Taming.StagFood + " in the first slots (eaten within " + Cfg.Taming.FoodSeconds + " s), then a saddle in the last slot.\n\n"
                + "<b>Others</b>\nCalf, lamb, chicken, crocodile, rabbit, squirrel, frog and crab have no taming flow. Use the transformation ritual or an admin.";
        }

        private string HelpCommands(BasePlayer p)
        {
            return "<b>Commanding tamed animals</b> " + OnOff(Cfg.Features.Commands) + "\n\n"
                + "follow  stays behind you (" + Cfg.Taming.FollowDistance + " m)\nwait  stands still\nguard  stays within " + Cfg.Taming.GuardRadius + " m and fights what threatens you\nattack  goes for what you aim at (or what threatens you)\nrelease  sets it free\n\n"
                + "<b>How</b>\n"
                + "Chat or console: /bm cmd follow  (aim at an animal's head for just that one; otherwise all animals within " + Cfg.Taming.CommandRange + " m)\n"
                + "The My animals page of this menu.\n"
                + "A Bone in hand: primary use gives the command, secondary use picks the next one.\n"
                + (Cfg.Taming.GestureCommands ? "Gestures on foot: ok = wait, wave = follow, thumbs up = guard, point = attack.\n" : "Gesture commands are switched off here.\n")
                + "\nYour team members are never attacked by your animals.";
        }

        private string HelpCombat(BasePlayer p)
        {
            return "<b>Combat</b> " + OnOff(Cfg.Features.Combat) + "\n\n"
                + "F attacks with the animal's own attacks, cycled in order, with the damage the game gives that animal (x" + Cfg.Combat.DamageScale + "). Cooldown " + Cfg.Combat.Cooldown + " s.\n"
                + "Look backwards more than " + Cfg.Combat.RearAngle + " degrees before pressing F: cows, bulls, sheep and rams kick behind them (rear attack).\n"
                + (Cfg.Combat.AutoAim ? "The attack turns the animal toward the nearest target in front of it.\n" : string.Empty)
                + "Attacks hurt: players " + OnOff(Cfg.Combat.HitPlayers) + ", animals " + OnOff(Cfg.Combat.HitAnimals) + ", buildings " + OnOff(Cfg.Combat.HitStructures) + ".\n"
                + (Cfg.Combat.Gather ? "Attacks also gather from trees, ore and corpses.\n" : string.Empty)
                + "Bulls and rams break wooden barricades when sprinting into them.\n"
                + "Wild animals you or your animal hurt run away; " + string.Join(", ", Cfg.Wild.AlwaysDefend.ToArray()) + " always fight back, the others do it with a " + Cfg.Wild.DefendChance + " % chance.\n"
                + "Bulls, rams, cows, sheep, stags, boars and horses that run into a player hurt and knock them down; into an animal they hurt it.";
        }

        private string HelpRitual(BasePlayer p)
        {
            return "<b>Ritual of transformation</b> " + OnOff(Cfg.Features.Transformation && Cfg.Features.AnimalForm) + "\n\n"
                + "1. <b>Kill</b> a wild animal of the kind you want to become (the body stays " + Mathf.RoundToInt(Cfg.Ritual.CorpseKeepSeconds / 60f) + " min and cannot be harvested).\n"
                + "2. <b>Circle</b> the body " + Cfg.Ritual.Circles + " times (" + Cfg.Ritual.BossCircles + " for the body of a boss) with a lit torch, closer than " + (Cfg.Ritual.Radius + 2.5f) + " m. After the last circle a low ring of fire burns round the body.\n"
                + "3. <b>Sacrifice</b>: within " + Cfg.Ritual.BurnSeconds + " s press E at the fire - or it goes out and takes the body. The fire burns whoever stands in it. It costs " + Mathf.RoundToInt(Cfg.Ritual.HealthCost * 100f) + " % of your current health and you bleed.\n"
                + "4. You are reborn as that animal, with its look and full health. Your human body is hidden and cannot be hurt.\n\n"
                + "<b>As the animal</b>\nYour health is the animal's health: if it dies, you die. Press X to return (smoke appears, then a " + Cfg.Form.Cooldown + " s cooldown). "
                + "Animals and turrets ignore you. You can open doors, loot, build, craft and use items (invisibly). Logging out makes the animal sleep where it stands.\n"
                + (Cfg.Ritual.RequireOwnKill ? "The body has to be of an animal you or your team killed.\n" : string.Empty)
                + (Cfg.Ritual.BlockInSafeZone ? "No ritual in safe zones.\n" : string.Empty)
                + "Restricted species (chicken, crocodile, rabbit, squirrel, frog) need the permission beastmaster.transform.restricted.";
        }

        private string HelpCamera(BasePlayer p)
        {
            return "<b>Camera</b> " + OnOff(Cfg.Features.ThirdPerson) + "\n\n"
                + "As an animal you play in third person only. Riding has both views: " + Cfg.Camera.DefaultRideView + " person at first, G or R switches and the choice is kept.\n"
                + "Look around with the mouse; the camera stays where you put it and turns with the animal. Turn it behind the animal for the rear attack.\n\n"
                + "<b>Animal camera</b> (Controls page, or /bm preset a|b)\n"
                + "A  spectator camera around the animal (default)\nB  A plus the game's own third-person flag\n"
                + "Height: /bm cam up | down | reset (yours), an admin keeps it for everybody with /bm cam save.\n\n"
                + "As an animal, interactions (doors, loot, food) start from the animal's head.";
        }

        private string HelpSpray(BasePlayer p)
        {
            return "<b>Spray can</b> " + OnOff(Cfg.Features.Spray) + "\n\n"
                + "Spray one of your animals with a Spray Can to roll its look again: the horse's breed, the livestock's coat and wool tint. Only the looks change - the gameplay genes (milk, wool, speed) are never touched.\n"
                + (Cfg.Spray.OwnerOnly ? "Only the owner and the team can re-roll an animal.\n" : "Anyone can re-roll an animal.\n")
                + "A re-rolled look is the look the transformation ritual copies later.";
        }

        private string HelpRelations(BasePlayer p)
        {
            return "<b>Animal relations</b> " + OnOff(Cfg.Features.Relations) + "\n\n"
                + "They apply when you are an animal.\n"
                + "A deer near a bear: the bear attacks. A bear near another bear: they tolerate each other, but a bear and a polar bear are enemies.\n"
                + "You are a bull: cows follow you. You are a ram: sheep follow you.\n"
                + "You are a wolf: other wolves keep within " + Cfg.Lead.BreakDistance + " m around you.\n"
                + "You are a bull or a ram: other bulls and rams fight you.\n"
                + "Everything else is the game's own behaviour. The matrix is in the config (Relations).";
        }

        private string HelpPermissions(BasePlayer p)
        {
            var sb = new StringBuilder();
            sb.Append("<b>Permissions</b>  (green = you have it)\n");
            foreach (string[] row in new[]
            {
                new[] { PermUse, "menu, own animals, commands, gear" }, new[] { PermRide, "ride the standard species" }, new[] { PermRideRestricted, "ride chicken, crocodile, snake, rabbit, squirrel, frog" },
                new[] { PermTame, "tame animals" }, new[] { PermTransform, "transformation ritual" }, new[] { PermTransformRestricted, "ritual for the restricted species" },
                new[] { PermSpray, "re-roll animals with the spray can" }, new[] { PermMenu, "control layer and spawning" }, new[] { PermAdmin, "everything" }
            })
            {
                sb.Append(Has(p, row[0]) ? "<color=#8fd36b>" : "<color=#d36b6b>").Append(row[0]).Append("</color>  ").Append(row[1]).Append('\n');
            }
            sb.Append("\n<b>Chat commands</b>\n/bm  menu    /bm help    /bm species    /bm keys    /bm view    /bm preset a|b|c|d    /bm revert\n/bm cmd follow|wait|guard|attack|release    /bm ride    /bm form (aimed animal)\n");
            sb.Append("\n<b>Server console / RCON</b>\nbm.enable  bm.disable  bm.grant <player>  bm.revoke <player>  bm.access  bm.grantgroup <group>  bm.revokegroup <group>\nbm.spawn <species> [count] [tamed|wild] [saddled] [near] [player]  bm.clear [radius]  bm.reload  bm.selftest  bm.debug <level>  bm.prefabs <words>");
            return sb.ToString();
        }

        private string HelpFaq(BasePlayer p)
        {
            return "<b>FAQ / troubleshooting</b>\n\n"
                + "<b>I cannot mount.</b> The animal must be tamed, fitted with a saddle and within " + Cfg.Riding.MountDistance + " m; you need the permission beastmaster.ride.\n\n"
                + "<b>The animal does not follow.</b> Its state may be wait or guard: /bm cmd follow. Followers further than " + (Cfg.Taming.TeleportDistance > 0f ? Cfg.Taming.TeleportDistance + " m are moved next to you." : "that stay behind.") + "\n\n"
                + "<b>The camera feels wrong.</b> As an animal: /bm cam up or down, or the other preset (/bm preset b). Riding: G or R switches between first and third person.\n\n"
                + "<b>The rider sits too high, too low, too far forward or back.</b> An admin sits on that species and types /bm seat up | down | forward | back (5 cm per step, kept for the species), or uses the Seat buttons on the Controls page.\n\n"
                + "<b>The saddle on the back sits wrong.</b> An admin looks at the animal and types /bm saddle (it tells what it found) and /bm saddle lift | scale | turn to fit it.\n\n"
                + "<b>The animal has no attack.</b> Rabbits, squirrels, frogs and crabs have no attack; F plays their call.\n\n"
                + "<b>How do I get back?</b> X returns you to human form. If you logged out as an animal, you wake up as the animal.\n\n"
                + "<b>F5 does nothing.</b> A server cannot bind keys in your game. Use Ctrl + R or /bm, or type bind f5 bm.menu once in the F1 console (the game keeps it).\n\n"
                + "<b>Something is broken.</b> Tell an admin: they can run bm.selftest and send you the result.";
        }

        // One species in a card: speeds, how to ride and tame, traits.
        private string SpeciesCard(SpeciesDef s)
        {
            if (s == null)
            {
                return "Pick a species.";
            }
            var sb = new StringBuilder();
            sb.Append("<size=17><b>").Append(s.Display).Append("</b></size>  ").Append(SpeciesEnabled(s) ? OnOff(true) : "<color=#d36b6b>not available</color>").Append("\n\n");
            if (!s.Available)
            {
                sb.Append(s.Note).Append('\n');
                return sb.ToString();
            }
            float scale = SpeedScaleOf(s);
            sb.Append("<b>Speed</b>  walk ").Append(Num(s.Walk * scale)).Append(" / sprint ").Append(Num(s.Sprint * scale)).Append(" / top ").Append(Num(s.Top * scale)).Append(" m/s\n");
            sb.Append("<b>Riding</b>  ").Append(s.Ride == RideTier.Standard ? "yes (beastmaster.ride)" : s.Ride == RideTier.Restricted ? "restricted (beastmaster.ride.restricted)" : "not rideable").Append("   <b>Gear</b>  ").Append(s.Gear ? "saddle, shoes, bag" : "none").Append('\n');
            sb.Append("<b>Taming</b>  ").Append(TamingOf(s)).Append('\n');
            sb.Append("<b>Transformation</b>  ").Append(s.Transform ? (s.TransformRestricted ? "yes (beastmaster.transform.restricted)" : "yes (beastmaster.transform)") : s.Id == "horse" ? "no - the horse form is not part of this build" : "no").Append('\n');
            sb.Append("<b>Diet</b>  ").Append(s.Food).Append(s.Predator ? ", predator" : string.Empty).Append(s.Swimmer ? ", swims" : string.Empty).Append(s.DrownProof ? ", cannot drown" : string.Empty).Append('\n');
            if (!string.IsNullOrEmpty(s.Trait))
            {
                sb.Append("<b>Trait</b>  ").Append(s.Trait).Append('\n');
            }
            if (s.Id == "bull" || s.Id == "ram" || s.Id == "stag")
            {
                sb.Append("Always defends itself when attacked.\n");
            }
            return sb.ToString();
        }

        private string TamingOf(SpeciesDef s)
        {
            switch (s.Tame)
            {
                case TameFlow.Bond: return s.Id == "horse" ? "bond - stand near a calm horse for " + Cfg.Horse.BondSeconds + " s" : "bond - stay near it for " + Cfg.Taming.BondSeconds + " s, then E";
                case TameFlow.Torch: return "chase it with a lit torch for " + Cfg.Taming.WolfChaseSeconds + " s";
                case TameFlow.Predator: return "dart, bear trap, " + Cfg.Taming.SyringesNeeded + " syringes";
                case TameFlow.SleepFeed: return "dart, then " + Cfg.Taming.FoodNeeded + " " + Cfg.Taming.StagFood + " and a saddle";
                case TameFlow.Admin: return "not by players (admin spawn or the ritual)";
                default: return "cannot be tamed";
            }
        }

        private void DrawHelp(CuiElementContainer c, string parent, BasePlayer player, MenuState st)
        {
            List<HelpTopic> all = HelpTopics();
            string needle = (st.Search ?? string.Empty).ToLowerInvariant();
            List<HelpTopic> shown = string.IsNullOrEmpty(needle)
                ? all
                : all.Where(t => t.Title.ToLowerInvariant().Contains(needle) || TagRegex.Replace(t.Text(player), string.Empty).ToLowerInvariant().Contains(needle)).ToList();
            // search box
            UiPanel(c, parent, parent + ".sbox", 0.015f, 0.925f, 0.285f, 0.985f, "0.05 0.06 0.04 1");
            c.Add(new CuiElement
            {
                Parent = parent + ".sbox",
                Components =
                {
                    new CuiInputFieldComponent { Text = st.Search ?? string.Empty, FontSize = 13, Align = TextAnchor.MiddleLeft, Command = "bm.ui search", CharsLimit = 30, Color = "1 1 1 1" },
                    new CuiRectTransformComponent { AnchorMin = "0.04 0", AnchorMax = "0.96 1" }
                }
            });
            if (string.IsNullOrEmpty(st.Search))
            {
                UiLabel(c, parent + ".sbox", "<color=#6f7a66>Search the Help ...</color>", 12, 0.04f, 0f, 0.8f, 1f, TextAnchor.MiddleLeft);
            }
            else
            {
                UiButton(c, parent, "x", "bm.ui clearsearch", 0.25f, 0.93f, 0.28f, 0.98f, ColOff, 11);
            }
            float y = 0.91f;
            foreach (HelpTopic t in shown)
            {
                UiButton(c, parent, t.Title, "bm.ui topic " + t.Id, 0.015f, y - 0.05f, 0.285f, y, st.Topic == t.Id ? ColNavOn : ColNav, 12);
                y -= 0.056f;
            }
            if (shown.Count == 0)
            {
                UiLabel(c, parent, "<color=#9aa58f>No topic matches.</color>", 13, 0.02f, 0.80f, 0.28f, 0.88f);
                return;
            }
            HelpTopic topic = shown.FirstOrDefault(t => t.Id == st.Topic) ?? shown[0];
            if (topic.Id != st.Topic)
            {
                st.Topic = topic.Id;
                st.TextPage = 0;
            }
            float top = 0.985f;
            int rows = 22;
            if (topic.Id == "species")
            {
                var species = AllSpecies.Where(s => s.Available).ToList();
                for (int i = 0; i < species.Count; i++)
                {
                    int col = i % 7, row = i / 7;
                    float x0 = 0.31f + col * 0.097f, y1 = 0.985f - row * 0.055f;
                    UiButton(c, parent, species[i].Display, "bm.ui species " + species[i].Id, x0, y1 - 0.05f, x0 + 0.092f, y1, st.Species == species[i].Id ? ColNavOn : ColNav, 11);
                }
                top = 0.985f - ((species.Count + 6) / 7) * 0.055f - 0.01f;
                rows = 15;
            }
            List<string> pages = PageText(topic.Text(player), rows, 78);
            st.TextPage = Mathf.Clamp(st.TextPage, 0, pages.Count - 1);
            UiLabel(c, parent, pages[st.TextPage], 13, 0.31f, 0.09f, 0.985f, top);
            if (pages.Count > 1)
            {
                UiButton(c, parent, "<", "bm.ui tpage -1", 0.31f, 0.02f, 0.37f, 0.08f, ColBtn, 14);
                UiLabel(c, parent, "<color=#9aa58f>page " + (st.TextPage + 1) + " / " + pages.Count + "</color>", 12, 0.38f, 0.02f, 0.62f, 0.08f, TextAnchor.MiddleCenter);
                UiButton(c, parent, ">", "bm.ui tpage 1", 0.63f, 0.02f, 0.69f, 0.08f, ColBtn, 14);
            }
            UiButton(c, parent, "What I'm looking at", "bm.ui lookhelp", 0.76f, 0.02f, 0.985f, 0.08f, ColBtn, 12);
        }

        // Splits a text into pages of about the given number of rows (a long line counts as several).
        private static List<string> PageText(string text, int rowsPerPage, int charsPerRow)
        {
            var pages = new List<string>();
            var cur = new StringBuilder();
            int rows = 0;
            foreach (string line in text.Replace("\r", string.Empty).Split('\n'))
            {
                int visible = TagRegex.Replace(line, string.Empty).Length;
                int n = Mathf.Max(1, Mathf.CeilToInt(visible / (float)charsPerRow));
                if (rows + n > rowsPerPage && rows > 0)
                {
                    pages.Add(cur.ToString().TrimEnd('\n'));
                    cur.Length = 0;
                    rows = 0;
                }
                cur.Append(line).Append('\n');
                rows += n;
            }
            if (cur.Length > 0)
            {
                pages.Add(cur.ToString().TrimEnd('\n'));
            }
            if (pages.Count == 0)
            {
                pages.Add(string.Empty);
            }
            return pages;
        }

        // The species of the animal the player looks at (a Gen2 animal or a horse).
        private SpeciesDef SpeciesInSight(BasePlayer player, out string note)
        {
            note = string.Empty;
            BaseNPC2 npc = AnimalInSight(player, 40f, 20f);
            if (npc != null)
            {
                SpeciesDef sp = SpeciesOf(npc);
                TamedRecord rec = RecordOf(npc);
                note = rec != null ? "This animal is tamed" + (rec.Owner == player.userID ? " - it is yours." : ".") : "This animal is wild.";
                return sp;
            }
            RidableHorse horse = HorseInSight(player, 40f, 20f);
            if (horse != null)
            {
                string label = HorseLabel(horse);
                note = label == null ? "This is an ordinary horse." : "You look at a " + label + " horse.";
                return SpeciesOf(horse);
            }
            return null;
        }

        // ---------------------------------------------------------------- My animals

        private void DrawAnimals(CuiElementContainer c, string parent, BasePlayer player, MenuState st)
        {
            List<TamedRecord> mine = Data.Tamed.Values.Where(r => r.Owner == player.userID).OrderBy(r => r.Species).ThenBy(r => r.Tag).ToList();
            UiLabel(c, parent, "<size=18><b>My animals</b></size>   " + mine.Count + " of " + Cfg.Taming.MaxOwned, 14, 0.02f, 0.91f, 0.7f, 0.99f, TextAnchor.MiddleLeft);
            UiLabel(c, parent, "All:", 12, 0.62f, 0.93f, 0.66f, 0.985f, TextAnchor.MiddleRight);
            string[] states = { "follow", "wait", "guard", "attack" };
            for (int i = 0; i < states.Length; i++)
            {
                UiButton(c, parent, states[i], "bm.ui all " + states[i], 0.67f + i * 0.08f, 0.93f, 0.745f + i * 0.08f, 0.985f, ColBtn, 11);
            }
            int perPage = 8;
            int pages = Mathf.Max(1, (mine.Count + perPage - 1) / perPage);
            st.ListPage = Mathf.Clamp(st.ListPage, 0, pages - 1);
            if (mine.Count == 0)
            {
                UiLabel(c, parent, "You do not own any animal yet.\n\nTame one - see Help - Taming. Tamed animals follow you, can wear a saddle and can be commanded here.", 14, 0.03f, 0.55f, 0.97f, 0.88f);
            }
            Vector3 here = player.transform.position;
            for (int i = 0; i < perPage; i++)
            {
                int index = st.ListPage * perPage + i;
                if (index >= mine.Count)
                {
                    break;
                }
                TamedRecord rec = mine[index];
                SpeciesDef sp = SpeciesById(rec.Species);
                BaseEntity e = EntityOfRecord(rec);
                float y1 = 0.89f - i * 0.105f;
                UiPanel(c, parent, parent + ".row" + i, 0.015f, y1 - 0.095f, 0.985f, y1, "0.17 0.19 0.14 0.9");
                string name = (sp != null ? sp.Display : rec.Species) + (string.IsNullOrEmpty(rec.Name) ? string.Empty : " '" + rec.Name + "'") + (rec.Boss ? " <color=#e6b84a>[boss]</color>" : string.Empty);
                BaseCombatEntity living = e as BaseCombatEntity;
                string where = e != null ? Mathf.RoundToInt(Vector3.Distance(here, e.transform.position)) + " m away" : "not loaded";
                string hp = living != null ? Mathf.RoundToInt(living.health) + " HP" : Mathf.RoundToInt(rec.Health) + " HP";
                string gear = rec.Gear.Saddle > 0 ? (rec.Gear.Saddle > 1 ? "double saddle" : "saddle") : "no saddle";
                UiLabel(c, parent + ".row" + i, "<b>" + name + "</b>\n<size=11><color=#b4bfa8>" + rec.State + "  |  " + where + "  |  " + hp + "  |  " + gear + "</color></size>", 13, 0.015f, 0f, 0.5f, 1f, TextAnchor.MiddleLeft);
                string[] cmds = { "follow", "wait", "guard", "name", "release" };
                for (int k = 0; k < cmds.Length; k++)
                {
                    bool release = cmds[k] == "release";
                    string label = release && st.ConfirmRelease == rec.Tag ? "Sure?" : cmds[k] == "name" ? "Name" : cmds[k];
                    string color = release ? ColOff : cmds[k] == "name" ? (st.RenameTag == rec.Tag ? ColNavOn : ColWarn) : rec.State == cmds[k] ? ColNavOn : ColBtn;
                    UiButton(c, parent + ".row" + i, label, "bm.ui animal " + rec.Tag + " " + cmds[k], 0.50f + k * 0.10f, 0.12f, 0.59f + k * 0.10f, 0.88f, color, 12);
                }
            }
            if (pages > 1)
            {
                UiButton(c, parent, "<", "bm.ui list -1", 0.40f, 0.015f, 0.46f, 0.06f, ColBtn, 14);
                UiLabel(c, parent, "<color=#9aa58f>page " + (st.ListPage + 1) + " / " + pages + "</color>", 12, 0.47f, 0.015f, 0.62f, 0.06f, TextAnchor.MiddleCenter);
                UiButton(c, parent, ">", "bm.ui list 1", 0.63f, 0.015f, 0.69f, 0.06f, ColBtn, 14);
            }
            TamedRecord renaming;
            if (st.RenameTag != 0UL && Data.Tamed.TryGetValue(st.RenameTag, out renaming) && renaming.Owner == player.userID)
            {
                // a name field over the list: type the name and press Enter
                UiPanel(c, parent, parent + ".rename", 0.10f, 0.40f, 0.90f, 0.62f, "0.03 0.04 0.02 0.97", true);
                UiLabel(c, parent + ".rename", "Name for your " + renaming.Species + " (Enter saves):", 14, 0.04f, 0.58f, 0.96f, 0.95f, TextAnchor.MiddleLeft);
                UiPanel(c, parent + ".rename", parent + ".renamebox", 0.04f, 0.22f, 0.76f, 0.56f, "0.10 0.11 0.08 1");
                c.Add(new CuiElement
                {
                    Parent = parent + ".renamebox",
                    Components =
                    {
                        new CuiInputFieldComponent { Text = renaming.Name ?? string.Empty, FontSize = 15, Align = TextAnchor.MiddleLeft, Command = "bm.ui renameset " + renaming.Tag, CharsLimit = 24, Color = "1 1 1 1" },
                        new CuiRectTransformComponent { AnchorMin = "0.03 0", AnchorMax = "0.97 1" }
                    }
                });
                UiButton(c, parent + ".rename", "Cancel", "bm.ui rename 0", 0.80f, 0.22f, 0.96f, 0.56f, ColOff, 13);
            }
        }

        // ---------------------------------------------------------------- Controls

        private void DrawControls(CuiElementContainer c, string parent, BasePlayer player, MenuState st)
        {
            PlayerSettings s = SettingsOf(player.userID);
            Driver d = DriverOf(player);
            UiLabel(c, parent, "<size=18><b>Controls and camera</b></size>", 14, 0.02f, 0.91f, 0.7f, 0.99f, TextAnchor.MiddleLeft);
            UiLabel(c, parent, HelpControls(player).Split(new[] { "\n\n" }, StringSplitOptions.None)[0], 13, 0.03f, 0.43f, 0.97f, 0.90f);
            string view = string.IsNullOrEmpty(s.View) ? Cfg.Camera.DefaultRideView : s.View;
            string preset = PresetOf(player) == "B" ? "B" : "A";
            UiLabel(c, parent, "View when riding:", 13, 0.03f, 0.34f, 0.28f, 0.41f, TextAnchor.MiddleLeft);
            UiButton(c, parent, "third person", "bm.ui view third", 0.29f, 0.35f, 0.47f, 0.41f, view == "third" ? ColNavOn : ColBtn);
            UiButton(c, parent, "first person", "bm.ui view first", 0.48f, 0.35f, 0.66f, 0.41f, view == "first" ? ColNavOn : ColBtn);
            if (d != null && d.Kind == ControlKind.Ride && IsAdmin(player))
            {
                UiButton(c, parent, "Seat up", "bm.ui seat up", 0.68f, 0.35f, 0.76f, 0.41f, ColBtn, 11);
                UiButton(c, parent, "Seat down", "bm.ui seat down", 0.765f, 0.35f, 0.845f, 0.41f, ColBtn, 11);
                UiButton(c, parent, "Seat fwd", "bm.ui seat forward", 0.85f, 0.35f, 0.915f, 0.41f, ColBtn, 11);
                UiButton(c, parent, "Seat back", "bm.ui seat back", 0.92f, 0.35f, 0.985f, 0.41f, ColBtn, 11);
            }
            UiLabel(c, parent, "Animal camera:", 13, 0.03f, 0.26f, 0.28f, 0.33f, TextAnchor.MiddleLeft);
            string[] presets = { "A", "B" };
            for (int i = 0; i < presets.Length; i++)
            {
                UiButton(c, parent, "preset " + presets[i], "bm.ui preset " + presets[i], 0.29f + i * 0.13f, 0.27f, 0.405f + i * 0.13f, 0.33f, preset == presets[i] ? ColNavOn : ColBtn);
            }
            UiButton(c, parent, "Cam up", "bm.ui cam up", 0.58f, 0.27f, 0.70f, 0.33f, ColBtn, 12);
            UiButton(c, parent, "Cam down", "bm.ui cam down", 0.71f, 0.27f, 0.83f, 0.33f, ColBtn, 12);
            UiButton(c, parent, "Cam reset", "bm.ui cam reset", 0.84f, 0.27f, 0.985f, 0.33f, ColBtn, 12);
            UiLabel(c, parent, "<size=11><color=#9aa58f>An animal is played in third person: A = spectator camera around the animal | B = A + the game's third-person flag. Riding has both views (G or R).</color></size>", 11, 0.03f, 0.20f, 0.97f, 0.26f);
            UiButton(c, parent, (s.RefuseSaddle ? "[x] " : "[  ] ") + "Refuse saddles from others while I am an animal", "bm.ui setting refuse", 0.03f, 0.12f, 0.55f, 0.19f, s.RefuseSaddle ? ColOn : ColBtn, 12);
            UiButton(c, parent, (s.ShowHud ? "[x] " : "[  ] ") + "Show the key hints", "bm.ui setting hud", 0.57f, 0.12f, 0.97f, 0.19f, s.ShowHud ? ColOn : ColBtn, 12);
            if (d != null)
            {
                UiButton(c, parent, d.Kind == ControlKind.Ride ? "Get off" : "Back to human", "bm.ui revert", 0.03f, 0.03f, 0.27f, 0.10f, ColWarn);
                string[][] actions = { new[] { "Lie", "lie" }, new[] { "Sit", "sit" }, new[] { "Roar", "roar" }, new[] { "Look", "look" }, new[] { "Stand", "stand" }, new[] { "Night", "nv" }, new[] { "Breed", "breed" } };
                for (int a = 0; a < actions.Length; a++)
                {
                    string command = actions[a][1] == "nv" || actions[a][1] == "breed" ? "bm.ui " + actions[a][1] : "bm.ui gesture " + actions[a][1];
                    UiButton(c, parent, actions[a][0], command, 0.29f + a * 0.098f, 0.03f, 0.375f + a * 0.098f, 0.10f, ColBtn, 12);
                }
            }
        }

        // ---------------------------------------------------------------- Transformation

        private void DrawRitual(CuiElementContainer c, string parent, BasePlayer player, MenuState st)
        {
            Driver d = DriverOf(player);
            var sb = new StringBuilder();
            sb.Append("<size=18><b>Transformation</b></size>  ").Append(OnOff(Cfg.Features.Transformation && Cfg.Features.AnimalForm)).Append("\n\n");
            sb.Append(HelpRitual(player).Split(new[] { "\n\n" }, 2, StringSplitOptions.None)[1]).Append("\n\n");
            sb.Append("<b>Your status</b>\n");
            long until;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (d != null)
            {
                sb.Append(d.Kind == ControlKind.Form ? "You are a " + d.Species.Display + ". Press X to return.\n" : "You are riding a " + d.Species.Display + ".\n");
            }
            else if (Data.RitualCooldown.TryGetValue(player.userID, out until) && until > now)
            {
                sb.Append("Cooldown: ").Append(until - now).Append(" s until you can transform again.\n");
            }
            else
            {
                sb.Append("Ready.\n");
            }
            foreach (RitualCorpse rc in RitualCorpses.Where(r => r.Killer == player.userID))
            {
                sb.Append("Body of a ").Append(rc.Species.Display).Append(": ").Append(Mathf.Max(0, Mathf.RoundToInt(Cfg.Ritual.CorpseKeepSeconds - (Time.time - rc.Died)))).Append(" s left").Append(rc.Burning ? ", <color=#e6b84a>burning - press E next to it</color>" : string.Empty).Append('\n');
            }
            UiLabel(c, parent, sb.ToString(), 13, 0.03f, 0.12f, 0.97f, 0.97f);
            if (d != null)
            {
                UiButton(c, parent, d.Kind == ControlKind.Ride ? "Get off" : "Back to human", "bm.ui revert", 0.03f, 0.03f, 0.30f, 0.10f, ColWarn);
            }
        }

        // ---------------------------------------------------------------- Admin (control layer)

        private void DrawAdmin(CuiElementContainer c, string parent, BasePlayer player, MenuState st)
        {
            UiLabel(c, parent, "<size=18><b>Admin</b></size>   switches are saved at once", 14, 0.02f, 0.91f, 0.8f, 0.99f, TextAnchor.MiddleLeft);
            UiButton(c, parent, "Plugin " + (Active ? "ON" : "OFF") + " (click to switch)", "bm.ui plugin", 0.03f, 0.83f, 0.40f, 0.90f, Active ? ColOn : ColOff, 13);
            UiLabel(c, parent, "<b>Features</b>", 13, 0.03f, 0.76f, 0.5f, 0.82f, TextAnchor.MiddleLeft);
            int i = 0;
            foreach (FieldInfo f in typeof(FeatureCfg).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.FieldType != typeof(bool))
                {
                    continue;
                }
                bool on = (bool)f.GetValue(Cfg.Features);
                int col = i % 4, row = i / 4;
                float x0 = 0.03f + col * 0.24f, y1 = 0.75f - row * 0.065f;
                UiButton(c, parent, Humanise(f.Name) + (on ? "  [on]" : "  [off]"), "bm.ui feature " + f.Name, x0, y1 - 0.057f, x0 + 0.23f, y1, on ? ColOn : ColOff, 11);
                i++;
            }
            float ys = 0.75f - ((i + 3) / 4) * 0.065f - 0.04f;
            UiLabel(c, parent, "<b>Species</b>", 13, 0.03f, ys - 0.06f, 0.5f, ys, TextAnchor.MiddleLeft);
            ys -= 0.06f;
            int k = 0;
            foreach (SpeciesDef s in AllSpecies)
            {
                int col = k % 5, row = k / 5;
                float x0 = 0.03f + col * 0.19f, y1 = ys - row * 0.065f;
                bool on = SpeciesEnabled(s);
                UiButton(c, parent, s.Display + (s.Available ? (on ? "  [on]" : "  [off]") : "  n/a"), s.Available ? "bm.ui species_toggle " + s.Id : "bm.ui noop", x0, y1 - 0.057f, x0 + 0.18f, y1, !s.Available ? "0.25 0.25 0.25 1" : on ? ColOn : ColOff, 11);
                k++;
            }
        }

        // ---------------------------------------------------------------- Spawn (control layer)

        private void DrawSpawn(CuiElementContainer c, string parent, BasePlayer player, MenuState st)
        {
            UiLabel(c, parent, "<size=18><b>Spawn animals</b></size>   " + OnOff(Cfg.Features.Spawning) + "   alive: " + SpawnedAlive() + " / " + Cfg.Spawn.MaxAlive, 14, 0.02f, 0.91f, 0.9f, 0.99f, TextAnchor.MiddleLeft);
            int k = 0;
            foreach (SpeciesDef s in AllSpecies.Where(SpeciesEnabled))
            {
                int col = k % 5, row = k / 5;
                float x0 = 0.03f + col * 0.19f, y1 = 0.89f - row * 0.075f;
                UiButton(c, parent, s.Display, "bm.ui spawnspecies " + s.Id, x0, y1 - 0.066f, x0 + 0.18f, y1, st.Species == s.Id ? ColNavOn : ColNav, 12);
                k++;
            }
            float y = 0.89f - ((k + 4) / 5) * 0.075f - 0.03f;
            SpeciesDef pick = SpeciesById(st.Species);
            UiLabel(c, parent, "Amount:", 13, 0.03f, y - 0.07f, 0.14f, y, TextAnchor.MiddleLeft);
            UiButton(c, parent, "-", "bm.ui spawncount -1", 0.15f, y - 0.065f, 0.20f, y - 0.005f, ColBtn, 15);
            UiLabel(c, parent, "<b>" + st.SpawnCount + "</b>", 15, 0.21f, y - 0.07f, 0.27f, y, TextAnchor.MiddleCenter);
            UiButton(c, parent, "+", "bm.ui spawncount 1", 0.28f, y - 0.065f, 0.33f, y - 0.005f, ColBtn, 15);
            UiButton(c, parent, st.SpawnTamed ? "tamed to me" : "wild", "bm.ui spawnmode", 0.36f, y - 0.065f, 0.54f, y - 0.005f, st.SpawnTamed ? ColOn : ColBtn, 12);
            UiButton(c, parent, (st.SpawnSaddled ? "[x] " : "[  ] ") + "saddled", "bm.ui spawnsaddle", 0.56f, y - 0.065f, 0.72f, y - 0.005f, st.SpawnSaddled ? ColOn : ColBtn, 12);
            y -= 0.10f;
            UiButton(c, parent, "Spawn " + (pick != null ? pick.Display : "?") + " where I aim", "bm.ui spawn aim", 0.03f, y - 0.07f, 0.34f, y, ColOn, 13);
            UiButton(c, parent, "Spawn next to me", "bm.ui spawn near", 0.36f, y - 0.07f, 0.60f, y, ColOn, 13);
            y -= 0.10f;
            UiButton(c, parent, "Remove spawned within 100 m", "bm.ui spawnclear near", 0.03f, y - 0.07f, 0.34f, y, ColWarn, 12);
            UiButton(c, parent, "Remove all spawned", "bm.ui spawnclear all", 0.36f, y - 0.07f, 0.60f, y, ColOff, 12);
            y -= 0.10f;
            UiLabel(c, parent, "<b>Bosses</b> (where I aim):", 13, 0.03f, y - 0.07f, 0.24f, y, TextAnchor.MiddleLeft);
            for (int b = 0; b < BossKinds.Length; b++)
            {
                SpeciesDef bs = SpeciesById(BossKinds[b]);
                UiButton(c, parent, bs != null ? bs.Display : BossKinds[b], "bm.ui spawnboss " + BossKinds[b], 0.25f + b * 0.13f, y - 0.07f, 0.37f + b * 0.13f, y, ColWarn, 12);
            }
            UiButton(c, parent, (st.SpawnMini ? "[x] " : "[  ] ") + "mini", "bm.ui bossmini", 0.78f, y - 0.07f, 0.92f, y, st.SpawnMini ? ColOn : ColBtn, 12);
            UiLabel(c, parent, "<size=11><color=#9aa58f>Limits: " + Cfg.Spawn.MaxPerAction + " per action, cooldown " + Cfg.Spawn.Cooldown + " s, at most " + Cfg.Spawn.MaxAlive + " alive. Every spawn is logged.</color></size>", 11, 0.03f, 0.01f, 0.97f, y - 0.08f);
        }

        // ---------------------------------------------------------------- Aim tools (control layer)

        private void DrawTools(CuiElementContainer c, string parent, BasePlayer player, MenuState st)
        {
            string note;
            SpeciesDef looked = SpeciesInSight(player, out note);
            UiLabel(c, parent, "<size=18><b>Aim tools</b></size>   act on the animal you look at (up to 60 m)", 14, 0.02f, 0.91f, 0.95f, 0.99f, TextAnchor.MiddleLeft);
            UiLabel(c, parent, looked != null ? "You look at: <b>" + looked.Display + "</b>  " + note : "<color=#9aa58f>Aim at an animal first, then press a button. The menu stays open.</color>", 13, 0.03f, 0.80f, 0.97f, 0.89f, TextAnchor.MiddleLeft);
            string[][] tools =
            {
                new[] { "Ride it", "ride", "Mount it as a rider (admins ride any animal)" },
                new[] { "Become it", "form", "Take its body as an animal form (X to return)" },
                new[] { "Tame it to me", "tame", "Make it yours" },
                new[] { "Release it", "release", "Free an animal of yours" },
                new[] { "Kill it", "kill", "Kills it at once" },
                new[] { "Despawn it", "despawn", "Removes it without a body" },
            };
            for (int i = 0; i < tools.Length; i++)
            {
                int col = i % 2, row = i / 2;
                float x0 = 0.03f + col * 0.49f, y1 = 0.74f - row * 0.15f;
                UiButton(c, parent, tools[i][0], "bm.ui aim " + tools[i][1], x0, y1 - 0.09f, x0 + 0.20f, y1, tools[i][1] == "kill" || tools[i][1] == "despawn" ? ColOff : ColBtn, 14);
                UiLabel(c, parent, "<size=12><color=#b4bfa8>" + tools[i][2] + "</color></size>", 12, x0 + 0.215f, y1 - 0.09f, x0 + 0.47f, y1, TextAnchor.MiddleLeft);
            }
            UiLabel(c, parent, "<b>Admin commands in the chat</b>\nAn admin can run any server command from the chat, on any server this plugin is on: <b>/bm run env.time 12</b> or simply <b>/bm env.time 12</b>.\n"
                + "Menu access for other players: Players page, or <b>/bm grant <name></b> and <b>/bm revoke <name></b>.", 13, 0.03f, 0.04f, 0.97f, 0.30f);
        }

        // ---------------------------------------------------------------- Players (control layer)

        private void DrawPlayers(CuiElementContainer c, string parent, BasePlayer player, MenuState st)
        {
            UiLabel(c, parent, "<size=18><b>Players</b></size>   menu access also allows spawning", 14, 0.02f, 0.91f, 0.9f, 0.99f, TextAnchor.MiddleLeft);
            var granted = new HashSet<string>(permission.GetPermissionUsers(PermMenu).Select(s => s.Split('(')[0]));
            List<BasePlayer> online = BasePlayer.activePlayerList.Where(p => p != null && !p.IsNpc).OrderBy(p => p.displayName).ToList();
            int perPage = 10;
            int pages = Mathf.Max(1, (online.Count + perPage - 1) / perPage);
            st.ListPage = Mathf.Clamp(st.ListPage, 0, pages - 1);
            for (int i = 0; i < perPage; i++)
            {
                int index = st.ListPage * perPage + i;
                if (index >= online.Count)
                {
                    break;
                }
                BasePlayer p = online[index];
                bool auto = IsOwner(p) || IsModerator(p) || permission.UserHasPermission(p.UserIDString, PermAdmin);
                bool has = granted.Contains(p.UserIDString);
                float y1 = 0.89f - i * 0.078f;
                UiPanel(c, parent, parent + ".p" + i, 0.015f, y1 - 0.07f, 0.985f, y1, "0.17 0.19 0.14 0.9");
                string note = auto ? "  <color=#e6b84a>(owner / moderator / admin: always)</color>" : has ? "  <color=#8fd36b>(granted)</color>" : string.Empty;
                UiLabel(c, parent + ".p" + i, p.displayName + "  <size=11><color=#9aa58f>" + p.UserIDString + "</color></size>" + note, 13, 0.015f, 0f, 0.7f, 1f, TextAnchor.MiddleLeft);
                if (!auto)
                {
                    UiButton(c, parent + ".p" + i, has ? "Revoke" : "Grant", "bm.ui " + (has ? "revoke " : "grant ") + p.UserIDString, 0.80f, 0.12f, 0.98f, 0.88f, has ? ColOff : ColOn, 12);
                }
            }
            if (pages > 1)
            {
                UiButton(c, parent, "<", "bm.ui list -1", 0.40f, 0.015f, 0.46f, 0.06f, ColBtn, 14);
                UiLabel(c, parent, "<color=#9aa58f>page " + (st.ListPage + 1) + " / " + pages + "</color>", 12, 0.47f, 0.015f, 0.62f, 0.06f, TextAnchor.MiddleCenter);
                UiButton(c, parent, ">", "bm.ui list 1", 0.63f, 0.015f, 0.69f, 0.06f, ColBtn, 14);
            }
            UiLabel(c, parent, "<size=11><color=#9aa58f>Offline players: bm.grant <steamid> in the console. Groups: bm.grantgroup <group>.</color></size>", 11, 0.02f, 0.015f, 0.38f, 0.075f);
        }

        // ---------------------------------------------------------------- button actions (console command bm.ui)

        internal void MenuAction(BasePlayer player, string[] args, Action<string> reply)
        {
            if (player == null || args.Length == 0)
            {
                return;
            }
            MenuState st = MenuOf(player);
            bool control = HasMenuAccess(player);
            string act = args[0].ToLowerInvariant();
            if (!Active && act != "close" && act != "plugin" && act != "noop" && act != "beast" && act != "skin" && act != "wheel")
            {
                return;
            }
            string arg1 = args.Length > 1 ? args[1] : string.Empty;
            int number;
            switch (act)
            {
                case "close":
                    CloseMenu(player);
                    return;
                case "noop":
                    return;
                case "page":
                    if (MenuPages.Any(p => p[0] == arg1) && (!IsControlPage(arg1) || control))
                    {
                        st.Page = arg1;
                        st.TextPage = 0;
                        st.ListPage = 0;
                        st.ConfirmRelease = 0;
                    }
                    break;
                case "topic":
                    st.Topic = arg1;
                    st.TextPage = 0;
                    break;
                case "species":
                    if (SpeciesById(arg1) != null)
                    {
                        st.Species = arg1;
                        st.Note = string.Empty;
                        st.Topic = "species";
                        st.TextPage = 0;
                    }
                    break;
                case "tpage":
                    if (int.TryParse(arg1, out number))
                    {
                        st.TextPage = Mathf.Max(0, st.TextPage + number);
                    }
                    break;
                case "list":
                    if (int.TryParse(arg1, out number))
                    {
                        st.ListPage = Mathf.Max(0, st.ListPage + number);
                    }
                    break;
                case "search":
                    st.Search = string.Join(" ", args.Skip(1).ToArray()).Trim();
                    st.TextPage = 0;
                    break;
                case "clearsearch":
                    st.Search = string.Empty;
                    break;
                case "lookhelp":
                    {
                        string note;
                        SpeciesDef sp = SpeciesInSight(player, out note);
                        if (sp == null)
                        {
                            Tell(player, "NotLooking", 40);
                        }
                        else
                        {
                            st.Page = "help";
                            st.Topic = "species";
                            st.Species = sp.Id;
                            st.Note = note;
                            st.Search = string.Empty;
                            st.TextPage = 0;
                        }
                        break;
                    }
                case "animal":
                    AnimalButton(player, st, args);
                    break;
                case "all":
                    if (CommandNames.Contains(arg1))
                    {
                        int n = ApplyCommand(player, arg1, true);
                        Tell(player, n > 0 ? "CommandSet" : "NoAnimalsNearby", n + " animal(s)", arg1);
                    }
                    break;
                case "view":
                    {
                        // the view of riding (an animal form is third person only)
                        SettingsOf(player.userID).View = arg1 == "first" ? "first" : "third";
                        Driver d = DriverOf(player);
                        if (d != null && d.Kind == ControlKind.Ride)
                        {
                            d.SetView(arg1 == "first" ? ViewKind.First : ViewKind.Third);
                        }
                        break;
                    }
                case "preset":
                    {
                        string preset = arg1.ToUpperInvariant();
                        if (preset == "A" || preset == "B")
                        {
                            Driver d = DriverOf(player);
                            if (d != null && d.Kind == ControlKind.Form) d.SetPreset(preset);
                            SettingsOf(player.userID).Preset = preset;
                        }
                        break;
                    }
                case "setting":
                    {
                        PlayerSettings s = SettingsOf(player.userID);
                        if (arg1 == "refuse") s.RefuseSaddle = !s.RefuseSaddle;
                        else if (arg1 == "hud")
                        {
                            s.ShowHud = !s.ShowHud;
                            if (s.ShowHud) DrawKeyHints(player); else ClearKeyHints(player);       // at once, not at the next redraw
                        }
                        break;
                    }
                case "beast":
                    BeastButton(player, args);
                    return;
                case "skin":
                    SkinButton(player, args);
                    return;
                case "wheel":
                    WheelButton(player, args);
                    return;
                case "seat":
                    if (arg1 == "up" || arg1 == "down" || arg1 == "forward" || arg1 == "back")
                    {
                        SeatCommand(player, new[] { arg1 }, text => TellRaw(player, text));
                    }
                    break;
                case "revert":
                    StopControl(player, "menu");
                    break;
                case "plugin":
                    if (control) SetActive(!Active);
                    break;
                case "feature":
                    if (control) ToggleFeature(arg1);
                    break;
                case "species_toggle":
                    if (control) ToggleSpecies(arg1);
                    break;
                case "spawnspecies":
                    if (SpeciesById(arg1) != null) st.Species = arg1;
                    break;
                case "spawncount":
                    if (int.TryParse(arg1, out number)) st.SpawnCount = Mathf.Clamp(st.SpawnCount + number, 1, Cfg.Spawn.MaxPerAction);
                    break;
                case "spawnmode":
                    st.SpawnTamed = !st.SpawnTamed;
                    break;
                case "bossmini":
                    st.SpawnMini = !st.SpawnMini;
                    break;
                case "spawnboss":
                    if (control && BossKinds.Contains(arg1) && Bosses != null)
                    {
                        BaseCombatEntity boss = Bosses.SpawnBoss(arg1, SpawnSpot(player), st.SpawnMini);
                        TellRaw(player, boss != null ? "A boss " + arg1 + (st.SpawnMini ? " (mini)" : string.Empty) + " stands where you aimed." : "The boss could not be created.");
                    }
                    break;
                case "aim":
                    if (control)
                    {
                        AimAction(player, arg1);
                    }
                    break;
                case "gesture":
                    {
                        Driver gd = DriverOf(player);
                        if (gd != null)
                        {
                            gd.OnGesture(arg1);
                        }
                        break;
                    }
                case "cam":
                    CameraCommand(player, new[] { arg1 }, text => TellRaw(player, text));
                    break;
                case "nv":
                    ToggleNightVision(player);
                    break;
                case "breed":
                    BreedCommand(player, text => TellRaw(player, text));
                    break;
                case "rename":
                    {
                        ulong tagToName;
                        st.RenameTag = ulong.TryParse(arg1, out tagToName) ? tagToName : 0UL;
                        break;
                    }
                case "renameset":
                    {
                        ulong tagToSet;
                        TamedRecord named;
                        if (ulong.TryParse(arg1, out tagToSet) && Data.Tamed.TryGetValue(tagToSet, out named) && named.Owner == player.userID && args.Length > 2)
                        {
                            SetAnimalName(EntityOfRecord(named), named, string.Join(" ", args.Skip(2).ToArray()));
                        }
                        st.RenameTag = 0UL;
                        break;
                    }
                case "spawnsaddle":
                    st.SpawnSaddled = !st.SpawnSaddled;
                    break;
                case "spawn":
                    if (control)
                    {
                        var spawnArgs = new List<string> { st.Species, st.SpawnCount.ToString(CultureInfo.InvariantCulture), st.SpawnTamed ? "tamed" : "wild" };
                        if (st.SpawnSaddled) spawnArgs.Add("saddled");
                        if (arg1 == "near") spawnArgs.Add("near");
                        SpawnCommand(player, spawnArgs.ToArray(), text => TellRaw(player, text));
                    }
                    break;
                case "spawnclear":
                    if (control)
                    {
                        Tell(player, "Cleared", ClearSpawned(player, arg1 == "near" ? 100f : 0f));
                    }
                    break;
                case "grant":
                case "revoke":
                    if (control && !string.IsNullOrEmpty(arg1))
                    {
                        GrantCommand(act == "grant", new[] { arg1 }, text => TellRaw(player, text));
                    }
                    break;
                default:
                    Log(3, "Menu: unknown action '" + act + "'");
                    break;
            }
            if (st.Open)
            {
                DrawMenu(player);
            }
        }

        private void AnimalButton(BasePlayer player, MenuState st, string[] args)
        {
            ulong tag;
            if (args.Length < 3 || !ulong.TryParse(args[1], out tag))
            {
                return;
            }
            TamedRecord rec;
            if (!Data.Tamed.TryGetValue(tag, out rec) || rec.Owner != player.userID)
            {
                return;
            }
            string cmd = args[2].ToLowerInvariant();
            BaseEntity e = EntityOfRecord(rec);
            if (cmd == "name")
            {
                st.RenameTag = st.RenameTag == tag ? 0UL : tag;
                return;
            }
            if (cmd == "release")
            {
                if (st.ConfirmRelease != tag)
                {
                    st.ConfirmRelease = tag;
                    return;
                }
                st.ConfirmRelease = 0;
                if (e != null)
                {
                    ReleaseAnimal(e);
                }
                else
                {
                    Data.Tamed.Remove(tag);
                }
                return;
            }
            st.ConfirmRelease = 0;
            if (cmd == "follow" || cmd == "wait" || cmd == "guard")
            {
                rec.State = cmd;
                if (e != null)
                {
                    ApplyState(e, rec);
                }
            }
        }

        private void ToggleFeature(string name)
        {
            FieldInfo f = typeof(FeatureCfg).GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f == null || f.FieldType != typeof(bool))
            {
                return;
            }
            f.SetValue(Cfg.Features, !(bool)f.GetValue(Cfg.Features));
            SaveConfig();
            Log(2, "Feature " + name + " is now " + f.GetValue(Cfg.Features));
        }

        private void ToggleSpecies(string id)
        {
            SpeciesDef s = SpeciesById(id);
            if (s == null)
            {
                return;
            }
            SpeciesOverride o;
            if (!Cfg.Species.TryGetValue(s.Id, out o))
            {
                o = new SpeciesOverride();
                Cfg.Species[s.Id] = o;
            }
            o.Enabled = !(o.Enabled ?? true);
            SaveConfig();
            Log(2, "Species " + s.Id + " is now " + (o.Enabled.Value ? "enabled" : "disabled"));
        }

        // ---------------------------------------------------------------- headless test

        private static readonly HashSet<string> MenuActionNames = new HashSet<string>
        {
            "close", "noop", "page", "topic", "species", "tpage", "list", "search", "clearsearch", "lookhelp", "animal", "all", "view", "preset", "setting",
            "revert", "plugin", "feature", "species_toggle", "spawnspecies", "spawncount", "spawnmode", "spawnsaddle", "spawn", "spawnclear", "grant", "revoke",
            "bossmini", "spawnboss", "aim", "gesture", "cam", "nv", "breed", "rename", "renameset", "seat", "beast", "skin", "wheel"
        };

        // Builds every page and every Help topic for a stand-in player, checks the JSON and that every button names a known action.
        internal string TestMenu()
        {
            var lines = new List<string>();
            var go = new GameObject("bm-menu-test");
            try
            {
                BasePlayer dummy = go.AddComponent<BasePlayer>();
                dummy.userID = 76561198000000001UL;
                dummy.UserIDString = "76561198000000001";
                dummy.displayName = "Tester";
                int built = 0, bytes = 0, buttons = 0, biggest = 0;
                var unknown = new HashSet<string>();
                foreach (bool control in new[] { false, true })
                {
                    foreach (string[] page in MenuPages)
                    {
                        List<string> topics = page[0] == "help" ? HelpTopics().Select(t => t.Id).ToList() : new List<string> { "overview" };
                        foreach (string topic in topics)
                        {
                            List<string> cards = topic == "species" ? AllSpecies.Where(s => s.Available).Select(s => s.Id).ToList() : new List<string> { "cow" };
                            foreach (string card in cards)
                            {
                                var st = new MenuState { Page = page[0], Topic = topic, Species = card };
                                CuiElementContainer c = BuildMenu(dummy, st, control);
                                string json = c.ToJson();
                                JToken parsed = JToken.Parse(json);
                                built++;
                                bytes += json.Length;
                                biggest = Mathf.Max(biggest, json.Length);
                                foreach (JToken cmd in parsed.SelectTokens("..command"))
                                {
                                    string text = (string)cmd;
                                    if (string.IsNullOrEmpty(text))
                                    {
                                        continue;
                                    }
                                    buttons++;
                                    string[] parts = text.Split(' ');
                                    if (parts[0] != "bm.ui" || parts.Length < 2 || !MenuActionNames.Contains(parts[1]))
                                    {
                                        unknown.Add(text);
                                    }
                                }
                            }
                        }
                    }
                }
                lines.Add((unknown.Count == 0 ? "PASS" : "FAIL") + " " + built + " windows built (" + bytes / 1024 + " kB in all, largest " + biggest / 1024 + " kB), " + buttons + " buttons" + (unknown.Count > 0 ? ", unknown actions: " + string.Join(" | ", unknown.ToArray()) : string.Empty));
                var sizes = HelpTopics().Select(t => t.Id + " " + PageText(t.Text(dummy), 22, 78).Count).ToArray();
                lines.Add("PASS help pages per topic: " + string.Join(", ", sizes));
            }
            catch (Exception ex)
            {
                lines.Add("FAIL " + ex);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
            return string.Join("\n", lines.ToArray());
        }

        // ---------------------------------------------------------------- module

        internal sealed class MenuModule : Module
        {
            public override int Order => 90;

            public override void OnDisable()
            {
                Bm.CloseAllMenus();
            }

            public override void Unload()
            {
                Bm.CloseAllMenus();
            }

            public override void OnPlayerDisconnected(BasePlayer player, string reason)
            {
                Bm.MenuStates.Remove(player.userID);
            }

            public override void OnPlayerDeath(BasePlayer player, HitInfo info)
            {
                MenuState st;
                if (Bm.MenuStates.TryGetValue(player.userID, out st) && st.Open)
                {
                    Bm.CloseMenu(player);
                }
            }
        }
    }
}

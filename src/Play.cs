using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Game.Rust.Cui;
using Rust.Ai.Gen2;
using UnityEngine;

namespace Oxide.Plugins
{
    public partial class Beastmaster
    {
        // ---------------------------------------------------------------- what a player does as an animal besides moving
        // Gestures (lie down, sit, roar, look around), the key hints beside the hotbar, night vision, the key binds and the camera height.

        // ---------------------------------------------------------------- gestures

        private static readonly string[] CallWords = { "roar", "growl", "bellow", "moo", "bleat", "howl", "bark", "neigh", "whinny", "snort", "squawk", "cluck", "hiss", "chirp", "scream", "snarl", "intimidate", "threat", "call" };
        private static readonly string[] LookWords = { "look", "sniff", "shake", "ear", "tail", "scratch", "stretch", "lick", "chew", "search", "paw", "stomp", "idle" };
        private static readonly string[] SitWords = { "sit" };
        private static readonly string[] LieWords = { "lie", "lay", "rest", "sleep" };
        internal static readonly HashSet<string> DirectGestures = new HashSet<string> { "roar", "call", "look", "sit", "lie", "stand" };

        private static string GestureCategory(string gesture)
        {
            switch (gesture)
            {
                case "wave": case "victory": case "thumbsup": case "beatchest": case "shush_vocal": case "chicken":
                    return "roar";
                case "ok": case "clap": case "petting": case "hat_tip":
                    return "sit";
                case "surrender": case "friendly": case "thumbsdown": case "cabbagepatch": case "sillydance": case "twist": case "sillydance2": case "raiseroof": case "hiphopdance":
                    return "lie";
                default:
                    return "look";
            }
        }

        // The gesture wheel and the commands /bm lie, sit, roar, look and stand: the animal plays what its species has, with the sounds that belong to the clip.
        internal void PerformGesture(Driver d, string gesture)
        {
            string g = (gesture ?? string.Empty).ToLowerInvariant();
            string category = DirectGestures.Contains(g) ? (g == "call" ? "roar" : g) : GestureCategory(g);
            if (category == "stand")
            {
                d.ClearPose();
                return;
            }
            if (d.Horse != null)
            {
                if (category == "roar")
                {
                    RpcGroup(d.Horse, "CLIENT_Stand");           // the horse rears up and neighs
                }
                return;
            }
            BaseNPC2 animal = d.Animal;
            if (animal == null)
            {
                return;
            }
            switch (category)
            {
                case "lie":
                case "sit":
                    d.StartPose(category);
                    return;
                case "roar":
                    d.ClearPose();
                    PlayOneOf(d, CallWords, "call");
                    return;
                default:
                    d.ClearPose();
                    PlayOneOf(d, LookWords, "look");
                    return;
            }
        }

        private void PlayOneOf(Driver d, string[] words, string kind)
        {
            BaseNPC2 animal = d.Animal;
            List<ClipRef> all = Clips(animal).Where(c => !Has(c, "death") && !Has(c, "attack") && !Has(c, "turn") && !Has(c, "knock")).ToList();
            List<ClipRef> matches = all.Where(c => words.Any(w => Has(c, w)) && !Has(c, "lie") && !Has(c, "sleep") && !Has(c, "graze") && !Has(c, "drink")).ToList();
            if (matches.Count == 0)
            {
                matches = d.IdleClips();
            }
            if (matches.Count == 0)
            {
                return;
            }
            d.PlayIdleClip(matches[d.NextGestureIndex() % matches.Count]);
        }

        // ---------------------------------------------------------------- the height of the camera

        // What the player added to or took from the camera height of this species (m), plus what the admin saved for everyone.
        internal float CamLiftOf(BasePlayer player, SpeciesDef species)
        {
            if (species == null)
            {
                return 0f;
            }
            float lift = 0f;
            SpeciesOverride o;
            if (Cfg.Species.TryGetValue(species.Id, out o) && o.PivotLift.HasValue)
            {
                lift += o.PivotLift.Value;
            }
            float mine;
            PlayerSettings s = player != null ? SettingsOf(player.userID) : null;
            if (s != null && s.CamLift != null && s.CamLift.TryGetValue(species.Id, out mine))
            {
                lift += mine;
            }
            return lift;
        }

        // /bm cam up | down | reset | save
        internal void CameraCommand(BasePlayer player, string[] args, Action<string> reply)
        {
            Driver d = DriverOf(player);
            if (d == null || d.Species == null)
            {
                reply("Ride an animal or become one first.");
                return;
            }
            string what = args.Length > 0 ? args[0].ToLowerInvariant() : string.Empty;
            PlayerSettings s = SettingsOf(player.userID);
            if (s.CamLift == null)
            {
                s.CamLift = new Dictionary<string, float>();
            }
            float now;
            s.CamLift.TryGetValue(d.Species.Id, out now);
            switch (what)
            {
                case "up": now += 0.25f; break;
                case "down": now -= 0.25f; break;
                case "reset": now = 0f; break;
                case "save":
                    if (!IsAdmin(player))
                    {
                        reply(L("NoPermission"));
                        return;
                    }
                    SpeciesOverride o;
                    if (!Cfg.Species.TryGetValue(d.Species.Id, out o))
                    {
                        o = new SpeciesOverride();
                        Cfg.Species[d.Species.Id] = o;
                    }
                    o.PivotLift = (o.PivotLift ?? 0f) + now;
                    s.CamLift[d.Species.Id] = 0f;
                    SaveConfig();
                    reply("Camera height of the " + d.Species.Display + " saved for everyone (" + o.PivotLift.Value.ToString("0.00") + " m).");
                    d.RefreshCamera();
                    return;
                default:
                    reply("Camera height: /bm cam up | down | reset" + (IsAdmin(player) ? " | save (keep it for everyone)" : string.Empty) + ". Now " + now.ToString("0.00") + " m for the " + d.Species.Display + ".");
                    return;
            }
            s.CamLift[d.Species.Id] = Mathf.Clamp(now, -1.5f, 4f);
            d.RefreshCamera();
            reply("Camera height of the " + d.Species.Display + ": " + s.CamLift[d.Species.Id].ToString("0.00") + " m.");
        }

        // ---------------------------------------------------------------- the key hints beside the hotbar

        private const string KeysUi = "BeastmasterKeys";

        // An animal form has no item belt: the panel of its keys takes the belt's place (an opaque panel, the belt is below it). A rider keeps the belt
        // and gets the keys beside it.
        internal void DrawKeyHints(BasePlayer player)
        {
            if (player == null || !player.IsConnected || !Cfg.Form.KeyHints || !SettingsOf(player.userID).ShowHud)
            {
                return;
            }
            Driver d = DriverOf(player);
            bool form = d != null && d.Kind == ControlKind.Form;
            if (form && !d.Spectating)
            {
                ClearKeyHints(player);                 // the inventory view shows the game's own HUD
                return;
            }
            bool hasSkin = form && d.HasSkins;
            var list = new List<string[]> { new[] { "F", "Attack" }, new[] { "S", "Brake" } };
            if (form)
            {
                list.Add(new[] { "E", "Feed" });
            }
            if (hasSkin)
            {
                list.Add(new[] { "R", "Skin" });
            }
            else if (!form)
            {
                list.Add(new[] { "R", "View" });
            }
            list.Add(new[] { "Shift", "Sprint" });
            list.Add(new[] { "G", "Jump" });
            list.Add(form ? new[] { "Space", "Human" } : new[] { "Space", "Get off" });
            if (form)
            {
                list.Add(new[] { "X", "Form" });
            }
            string[][] cells = list.ToArray();
            // one small row just above the item belt; each cell is as wide as its key and its word, the cells close together
            float cellH = 20f, gap = 2f;
            var widths = new float[cells.Length];
            float panelW = gap;
            for (int i = 0; i < cells.Length; i++)
            {
                widths[i] = 8f + 6f * cells[i][0].Length + 6f + 5.6f * cells[i][1].Length + 4f;
                panelW += widths[i] + gap;
            }
            float panelH = cellH + 2f * gap;
            var c = new CuiElementContainer();
            c.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.35" },
                RectTransform = { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = Pt(-panelW * 0.5f, 92f), OffsetMax = Pt(panelW * 0.5f, 92f + panelH) }
            }, "Overlay", KeysUi);
            float cx = gap;
            for (int i = 0; i < cells.Length; i++)
            {
                float cellW = widths[i];
                string key = cells[i][0];
                float cap = 8f + 6f * key.Length;
                string cell = KeysUi + ".c" + i;
                c.Add(new CuiPanel
                {
                    Image = { Color = "0 0 0 0.45" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = Pt(cx, gap), OffsetMax = Pt(cx + cellW, gap + cellH) }
                }, KeysUi, cell);
                c.Add(new CuiPanel
                {
                    Image = { Color = "0.89 0.91 0.83 1" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = Pt(2, 2), OffsetMax = Pt(2 + cap, cellH - 2) }
                }, cell, cell + ".k");
                c.Add(new CuiLabel
                {
                    Text = { Text = "<b>" + key + "</b>", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "0.09 0.11 0.07 1" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
                }, cell + ".k");
                c.Add(new CuiLabel
                {
                    Text = { Text = cells[i][1], FontSize = 11, Align = TextAnchor.MiddleLeft, Color = "0.94 0.95 0.9 1" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = Pt(cap + 5, 0), OffsetMax = Pt(cellW - 2, cellH) }
                }, cell);
                cx += cellW + gap;
            }
            CuiHelper.DestroyUi(player, KeysUi);
            CuiHelper.AddUi(player, c);
        }

        internal void ClearKeyHints(BasePlayer player)
        {
            if (player != null && player.IsConnected)
            {
                CuiHelper.DestroyUi(player, KeysUi);
            }
        }

        // ---------------------------------------------------------------- the inventory shows the animal

        private const string InvUi = "BeastmasterInventory";

        // Elements on the game's menu layer exist only while the inventory is open: a panel over the player's model and avatar shows the animal.
        internal void DrawInventoryCard(Driver d)
        {
            ClearInventoryCard(d.Player);          // the picture over the inventory is gone for good; the inventory opens as usual
        }

        internal void ClearInventoryCard(BasePlayer p)
        {
            if (p != null && p.IsConnected)
            {
                CuiHelper.DestroyUi(p, InvUi);
                CuiHelper.DestroyUi(p, InvUi + ".icon");
            }
        }

        // ---------------------------------------------------------------- night vision (H)

        private const ulong NightVisionSkin = 71990UL;

        private static Item FindNightVision(BasePlayer p)
        {
            foreach (Item it in PlayerItems(p))
            {
                if (it.skin == NightVisionSkin)
                {
                    return it;
                }
            }
            return null;
        }

        internal void ClearNightVision(BasePlayer p)
        {
            if (p == null || p.inventory == null)
            {
                return;
            }
            Item it;
            int guard = 0;
            while ((it = FindNightVision(p)) != null && guard++ < 4)
            {
                it.Remove();
            }
        }

        // The goggles of the game give the vision, so the player needs a pair in the inventory: Ctrl + G (or H after a bind, or the menu button)
        // puts them on the head and takes them off again.
        internal void ToggleNightVision(BasePlayer player)
        {
            Driver d = DriverOf(player);
            if (d == null)
            {
                TellRaw(player, "Night vision is for animal forms and riders.");
                return;
            }
            ItemContainer wear = player.inventory.containerWear;
            Item on = wear != null ? wear.itemList.FirstOrDefault(i => i.info.shortname == "nightvisiongoggles") : null;
            if (on != null)
            {
                if (!on.MoveToContainer(player.inventory.containerMain) && !on.MoveToContainer(player.inventory.containerBelt))
                {
                    TellRaw(player, "No room in the inventory to take the goggles off.");
                    return;
                }
                TellRaw(player, "Night vision off.");
                return;
            }
            Item goggles = PlayerItems(player).FirstOrDefault(i => i.info.shortname == "nightvisiongoggles");
            if (goggles == null)
            {
                TellRaw(player, "You need night vision goggles in your inventory.");
                return;
            }
            if (!goggles.MoveToContainer(wear))
            {
                TellRaw(player, "Take off your headgear first: the goggles need the head slot.");
                return;
            }
            TellRaw(player, "Night vision on.");
        }

        // Night vision of an animal form comes by itself: at night the goggles the player carries go onto the hidden head, by day (and when the form
        // ends) they go back into the inventory. No key.
        private readonly HashSet<ulong> autoNv = new HashSet<ulong>();
        private float nextNvCheck;

        internal void AutoNightVision()
        {
            if (Time.time < nextNvCheck || (Drivers.Count == 0 && autoNv.Count == 0))
            {
                return;
            }
            nextNvCheck = Time.time + 2f;
            bool night = IsNight();
            var wanted = new HashSet<ulong>();
            foreach (Driver d in Drivers.Values.ToList())
            {
                if (d == null || !d.Alive || d.Kind != ControlKind.Form || d.Player == null || !d.Player.IsConnected || !night)
                {
                    continue;
                }
                BasePlayer p = d.Player;
                ItemContainer wear = p.inventory.containerWear;
                if (wear == null)
                {
                    continue;
                }
                if (wear.itemList.Any(i => i.info.shortname == "nightvisiongoggles"))
                {
                    wanted.Add(p.userID);
                    continue;
                }
                Item goggles = PlayerItems(p).FirstOrDefault(i => i.info.shortname == "nightvisiongoggles");
                if (goggles != null && goggles.MoveToContainer(wear))
                {
                    autoNv.Add(p.userID);
                    wanted.Add(p.userID);
                }
            }
            foreach (ulong id in autoNv.ToList())
            {
                if (wanted.Contains(id))
                {
                    continue;
                }
                autoNv.Remove(id);
                BasePlayer p = BasePlayer.FindByID(id) ?? BasePlayer.FindSleeping(id);
                Item on = p != null && p.inventory != null && p.inventory.containerWear != null ? p.inventory.containerWear.itemList.FirstOrDefault(i => i.info.shortname == "nightvisiongoggles") : null;
                if (on != null && !on.MoveToContainer(p.inventory.containerMain))
                {
                    on.MoveToContainer(p.inventory.containerBelt);
                }
            }
        }

        // ---------------------------------------------------------------- key binds

        // A game only accepts a few commands from a server, and a key bind is not one of them: the key is bound once by the player (it is kept).
        internal void JoinHint(BasePlayer player)
        {
            if (!Cfg.General.JoinHint || player == null || !player.IsConnected || !Has(player, PermUse))
            {
                return;
            }
            PlayerSettings s = SettingsOf(player.userID);
            if (s.HintShown)
            {
                return;
            }
            s.HintShown = true;
            TellRaw(player, "Beastmaster menu: /bm, or hold Ctrl and press R (empty hands, or while riding or as an animal). Night vision as an animal comes by itself at night when you carry night vision goggles.");
        }

        // The server cannot rebind keys by itself, but it can ask the player's game to: the menu on F5 and the night vision on H.
        internal void SendBinds(BasePlayer player)
        {
            if (!Cfg.Controls.AutoBind || player == null || !player.IsConnected || !Has(player, PermUse))
            {
                return;
            }
            if (!string.IsNullOrEmpty(Cfg.Controls.MenuKey))
            {
                player.SendConsoleCommand("bind " + Cfg.Controls.MenuKey + " bm.menu");
            }
            if (!string.IsNullOrEmpty(Cfg.Controls.NightKey))
            {
                player.SendConsoleCommand("bind " + Cfg.Controls.NightKey + " bm.nv");
            }
        }

        // ---------------------------------------------------------------- R: another skin (colours and patterns) at once

        private readonly Dictionary<ulong, float> nextSkin = new Dictionary<ulong, float>();

        internal void ReskinForm(Driver d)
        {
            float last;
            if (nextSkin.TryGetValue(d.Player.userID, out last) && Time.time < last)
            {
                return;
            }
            nextSkin[d.Player.userID] = Time.time + 0.3f;
            BaseCombatEntity body = d.Body;
            if (!RerollLook(body))
            {
                return;
            }
            LivestockAnimal livestock = body as LivestockAnimal;
            if (livestock != null)
            {
                // the pattern comes from the name (rolled above), the colours and the size of the patches from these genes
                int genes = livestock.Genes;
                foreach (LivestockGene gene in new[] { LivestockGene.Dung, LivestockGene.Yield, LivestockGene.Longevity, LivestockGene.Fertility, LivestockGene.Hardiness })
                {
                    genes = LivestockGenome.WithPair(genes, gene, (LivestockAllele)UnityEngine.Random.Range(0, 3), (LivestockAllele)UnityEngine.Random.Range(0, 3));
                }
                livestock.Genes = genes;
            }
            body.SendNetworkUpdateImmediate();
        }

        // ---------------------------------------------------------------- the menu's tools on the animal in sight

        internal BaseEntity AimedAnimal(BasePlayer player)
        {
            return (BaseEntity)AnimalInSight(player, 60f, 12f) ?? HorseInSight(player, 60f, 12f);
        }

        internal void Despawn(BaseEntity e)
        {
            if (!Valid(e))
            {
                return;
            }
            BasePlayer rider = null;
            Driver d = DriverOfAnimal(e);
            if (d != null)
            {
                rider = d.Player;
                StopControl(rider, "despawned");
            }
            StopFollower(e);
            Data.Tamed.Remove(e.skinID);
            Data.Spawned.Remove(e.skinID);
            Data.Bosses.Remove(e.skinID);
            Data.Horses.Remove(e.skinID);
            RidableHorse horse = e as RidableHorse;
            if (horse != null && Stable != null)
            {
                Stable.Forget(horse);
            }
            e.Kill();
        }

        internal void AimAction(BasePlayer player, string what)
        {
            BaseEntity target = AimedAnimal(player);
            BaseCombatEntity living = target as BaseCombatEntity;
            if (living == null)
            {
                Tell(player, "NotLooking", 60);
                return;
            }
            switch (what)
            {
                case "ride":
                    StartControl(player, living, ControlKind.Ride);
                    break;
                case "form":
                    if (StartControl(player, living, ControlKind.Form))
                    {
                        Driver d = DriverOf(player);
                        if (d != null)
                        {
                            d.OwnsAnimal = false;
                        }
                    }
                    break;
                case "tame":
                    TameAnimal(target, player);
                    break;
                case "release":
                    if (RecordOf(target) != null)
                    {
                        ReleaseAnimal(target);
                    }
                    break;
                case "kill":
                    living.Hurt(living.health + 100000f, Rust.DamageType.Generic, player, false);
                    break;
                case "despawn":
                    Despawn(target);
                    break;
            }
            Log(2, player.displayName + " used the aim tool '" + what + "' on " + target.ShortPrefabName);
        }

        // ---------------------------------------------------------------- giving an item to an animal (it goes into its saddle bag)

        internal void GiveCommand(BasePlayer player, Action<string> reply)
        {
            BaseEntity target = (BaseEntity)AnimalInSight(player, 6f, 30f) ?? HorseInSight(player, 6f, 30f);
            Item held = player.GetActiveItem();
            if (target == null)
            {
                reply("Look at an animal (within 6 m) and hold the item you want to give.");
                return;
            }
            if (held == null)
            {
                reply("Hold the item you want to give.");
                return;
            }
            string name = held.info.displayName.english;
            Driver host = DriverOfAnimal(target);
            string why;
            if (host != null && host.Kind == ControlKind.Form && host.Player != player)
            {
                if (!FormHandledBy(host, player))
                {
                    reply("This animal does not take anything from you.");
                    return;
                }
                if (!StoreForForm(host, held, out why))
                {
                    reply(why);
                    return;
                }
                reply("You put " + name + " into the saddle bag of the " + host.Species.Display.ToLowerInvariant() + ".");
                TellRaw(host.Player, player.displayName + " put " + name + " into your saddle bag.");
                return;
            }
            if (target is RidableHorse)
            {
                reply("Hold E at the horse and open its inventory.");
                return;
            }
            GearRecord gear;
            string problem = InventoryProblem(player, target, out gear);
            if (problem != null)
            {
                reply(problem);
                return;
            }
            if (!AddToGear(gear, held, out why))
            {
                ForgetEmptyWild(gear);
                reply(why);
                return;
            }
            reply("You put " + name + " into its saddle bag.");
        }

        // ---------------------------------------------------------------- naming a tamed animal

        internal void RenameCommand(BasePlayer player, string[] args, Action<string> reply)
        {
            if (args.Length == 0)
            {
                reply("Usage: /bm name <new name> while you look at an animal of yours.");
                return;
            }
            BaseEntity target = (BaseEntity)AnimalInSight(player, 14f, 25f) ?? HorseInSight(player, 14f, 25f);
            TamedRecord rec = target != null ? RecordOf(target) : null;
            if (rec == null || !MayUse(player, rec.Owner, "owner"))
            {
                reply("Look at a tamed animal of yours first.");
                return;
            }
            string name = string.Join(" ", args).Trim();
            SetAnimalName(target, rec, name);
            reply("It is called " + rec.Name + " now.");
        }

        internal void SetAnimalName(BaseEntity e, TamedRecord rec, string name)
        {
            name = TagRegex.Replace(name ?? string.Empty, string.Empty).Trim();
            if (name.Length > 24)
            {
                name = name.Substring(0, 24);
            }
            rec.Name = name;
            LivestockAnimal ls = e as LivestockAnimal;
            if (ls != null)
            {
                ls.AnimalName = name;
                ls.SendNetworkUpdate();
            }
        }

        internal sealed class PlayModule : Module
        {
            public override int Order => 12;

            public override void Init(Beastmaster bm)
            {
                base.Init(bm);
                bm.Subscribe("OnPlayerRespawned");
            }

            public override void OnPlayerConnected(BasePlayer player)
            {
                BasePlayer p = player;
                Bm.timer.Once(6f, () => Bm.SendBinds(p));
                Bm.timer.Once(9f, () => Bm.JoinHint(p));
                Bm.timer.Once(5f, () => { if (p != null && Bm.DriverOf(p) == null) Bm.HudSwitch(p, true); });       // a HUD left off by an earlier session comes back
            }

            public override void OnPlayerRespawned(BasePlayer player)
            {
                BasePlayer p = player;
                Bm.timer.Once(3f, () => Bm.SendBinds(p));
            }

            private readonly Dictionary<ulong, float> nextCombo = new Dictionary<ulong, float>();

            // Ctrl + R opens the menu without any key bind (empty hands, or while riding or in animal form).
            public override object OnPlayerInput(BasePlayer player, InputState input)
            {
                if (!input.WasJustPressed(BUTTON.RELOAD) || !input.IsDown(BUTTON.DUCK))
                {
                    return null;
                }
                if (Bm.DriverOf(player) != null || player.GetActiveItem() != null)
                {
                    return null;                       // riders and animal forms: the driver reads Ctrl + R itself
                }
                float last;
                if (nextCombo.TryGetValue(player.userID, out last) && Time.time < last)
                {
                    return null;
                }
                nextCombo[player.userID] = Time.time + 0.6f;
                Bm.OpenMenu(player, "home");
                return null;
            }

            public override void OnServerInitialized()
            {
                foreach (BasePlayer p in BasePlayer.activePlayerList.ToList())
                {
                    BasePlayer target = p;
                    Bm.timer.Once(2f, () => Bm.SendBinds(target));
                }
            }

            public override void OnDisable()
            {
                Cleanup();
            }

            public override void Unload()
            {
                Cleanup();
            }

            private void Cleanup()
            {
                foreach (BasePlayer p in BasePlayer.activePlayerList.ToList())
                {
                    if (p != null && p.IsConnected)
                    {
                        Bm.ClearKeyHints(p);
                        Bm.ClearInventoryCard(p);
                        Bm.ClearNightVision(p);
                    }
                }
            }

            private float nextRedraw;

            // The hints are drawn when the control starts; they are sent again now and then in case the player's game dropped its UI.
            public override void Tick(float now)
            {
                if (now < nextRedraw || Bm.Drivers.Count == 0)
                {
                    return;
                }
                nextRedraw = now + 20f;
                foreach (Driver d in Bm.Drivers.Values.ToList())
                {
                    if (d != null && d.Alive && d.Player != null)
                    {
                        Bm.DrawKeyHints(d.Player);
                        Bm.DrawInventoryCard(d);
                    }
                }
            }
        }
    }
}

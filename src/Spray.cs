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
        // ---------------------------------------------------------------- spray can (R11)
        // Spraying an animal with a Spray Can rolls its look again: the horse's breed, the livestock's coat pattern and lineage markers.
        // The gameplay genes (milk, wool, speed ...) are never touched. The re-rolled look is the one the transformation ritual copies.

        private static readonly string[] SprayNames =
        {
            "Daisy", "Bella", "Clover", "Maple", "Poppy", "Willow", "Hazel", "Juniper", "Rosie", "Biscuit", "Pepper", "Ginger", "Olive", "Nutmeg",
            "Barley", "Copper", "Dusty", "Ember", "Flint", "Moss", "Sable", "Storm", "Thistle", "Tansy", "Bramble", "Cinder"
        };

        // The coat markers that exist: the ones the game itself rolls for a calf or a lamb (sampled once), so that the window offers no look
        // that is not in the game.
        private static int[] markerPool;

        internal static int[] MarkerPool()
        {
            if (markerPool == null)
            {
                var seen = new HashSet<int>();
                for (int i = 0; i < 600; i++)
                {
                    seen.Add(LivestockGenome.RollMarker());
                }
                markerPool = seen.OrderBy(x => x).ToArray();
            }
            return markerPool;
        }

        internal bool RerollLook(BaseEntity animal)
        {
            RidableHorse horse = animal as RidableHorse;
            if (horse != null)
            {
                if (horse.breeds == null || horse.breeds.Length < 2)
                {
                    return false;
                }
                int current = horse.GetBreed() != null ? Array.IndexOf(horse.breeds, horse.GetBreed()) : -1;
                int next;
                do
                {
                    next = UnityEngine.Random.Range(0, horse.breeds.Length);
                }
                while (next == current);
                horse.SetBreed(next);
                return true;
            }
            LivestockAnimal livestock = animal as LivestockAnimal;
            if (livestock != null)
            {
                string name;
                do
                {
                    name = SprayNames[UnityEngine.Random.Range(0, SprayNames.Length)] + (UnityEngine.Random.value < 0.5f ? string.Empty : " " + UnityEngine.Random.Range(2, 99));
                }
                while (name == livestock.AnimalName);
                livestock.AnimalName = name;
                int[] pool = MarkerPool();
                int a = pool[UnityEngine.Random.Range(0, pool.Length)];
                int b = pool[UnityEngine.Random.Range(0, pool.Length)];
                if (b == a && pool.Length > 1)
                {
                    b = pool[(Array.IndexOf(pool, a) + 1) % pool.Length];
                }
                livestock.Genes = LivestockGenome.WithMarkers(livestock.Genes, a, b);
                return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- the window of looks (R held)
        // The horse: its breeds. Cow, bull, sheep, ram: the two colours (16 each), the pattern (it follows the animal's name) and the size of the
        // patches. Each button applies at once; the window closes itself after a while or when the player is no longer on or in that animal.

        private const string SkinUi = "BeastmasterSkins";

        private sealed class SkinState
        {
            public BaseEntity Target;
            public int ColourA, ColourB, Pattern;
            public float Until;
        }

        private readonly Dictionary<ulong, SkinState> skinMenus = new Dictionary<ulong, SkinState>();

        // May this player change the look of that animal right now? (he is it, rides it, or rides the horse)
        private bool MaySkin(BasePlayer player, BaseEntity animal)
        {
            if (player == null || !Valid(animal))
            {
                return false;
            }
            Driver d = DriverOf(player);
            if (d != null && d.Body == animal)
            {
                return true;
            }
            BaseMountable seat = player.GetMounted();
            RidableHorse horse = animal as RidableHorse;
            return horse != null && seat != null && (seat == horse || seat.GetParentEntity() == horse) && horse.GetDriver() == player;
        }

        internal void OpenSkinMenu(BasePlayer player, BaseEntity animal)
        {
            if (!MaySkin(player, animal))
            {
                return;
            }
            RidableHorse horse = animal as RidableHorse;
            LivestockAnimal livestock = animal as LivestockAnimal;
            if ((horse == null || horse.breeds == null || horse.breeds.Length < 2) && livestock == null)
            {
                TellRaw(player, "This animal has no other looks.");
                return;
            }
            SkinState st;
            if (!skinMenus.TryGetValue(player.userID, out st) || st.Target != animal)
            {
                st = new SkinState { Target = animal, ColourA = 0, ColourB = 1, Pattern = livestock != null ? Mathf.Max(0, Array.IndexOf(SprayNames, livestock.AnimalName)) : 0 };
                skinMenus[player.userID] = st;
            }
            st.Until = Time.time + 30f;
            DrawSkinMenu(player, st);
        }

        private void DrawSkinMenu(BasePlayer player, SkinState st)
        {
            CuiHelper.DestroyUi(player, SkinUi);
            var c = new CuiElementContainer();
            RidableHorse horse = st.Target as RidableHorse;
            SpeciesDef species = SpeciesOf(st.Target);
            c.Add(new CuiPanel { Image = { Color = "0 0 0 0.75" }, RectTransform = { AnchorMin = "0.36 0.3", AnchorMax = "0.64 0.72" }, CursorEnabled = true }, "Overlay", SkinUi);
            UiLabel(c, SkinUi, "<b>Look of the " + (species != null ? species.Display.ToLowerInvariant() : "animal") + "</b>", 15, 0.04f, 0.88f, 0.96f, 0.98f, TextAnchor.MiddleCenter);
            if (horse != null)
            {
                int current = horse.GetBreed() != null ? Array.IndexOf(horse.breeds, horse.GetBreed()) : -1;
                int n = horse.breeds.Length;
                for (int i = 0; i < n; i++)
                {
                    int col = i % 2, row = i / 2;
                    float x0 = 0.05f + col * 0.46f, y1 = 0.85f - row * 0.13f;
                    string name = horse.breeds[i] != null ? horse.breeds[i].name : "Breed " + (i + 1);
                    UiButton(c, SkinUi, name, "bm.ui skin breed " + i, x0, y1 - 0.11f, x0 + 0.44f, y1, i == current ? ColOn : ColBtn, 12);
                }
            }
            else
            {
                Action<string, string, float> row = (label, cmd, y) =>
                {
                    UiLabel(c, SkinUi, label, 13, 0.05f, y - 0.1f, 0.5f, y, TextAnchor.MiddleLeft);
                    UiButton(c, SkinUi, "<", "bm.ui skin " + cmd + " -1", 0.55f, y - 0.1f, 0.7f, y, ColBtn, 14);
                    UiButton(c, SkinUi, ">", "bm.ui skin " + cmd + " 1", 0.78f, y - 0.1f, 0.93f, y, ColBtn, 14);
                };
                int count = MarkerPool().Length;
                if (count > 1)
                {
                    row("Colour 1:  " + (st.ColourA % count + 1) + " / " + count, "a", 0.82f);
                    row("Colour 2:  " + (st.ColourB % count + 1) + " / " + count, "b", 0.66f);
                }
                UiButton(c, SkinUi, "Random look", "bm.ui skin random", 0.20f, 0.30f, 0.80f, 0.43f, ColBtn, 12);
            }
            UiButton(c, SkinUi, "Close", "bm.ui skin close", 0.35f, 0.04f, 0.65f, 0.14f, ColOff, 12);
            CuiHelper.AddUi(player, c);
        }

        internal void CloseSkinMenu(BasePlayer player)
        {
            if (player != null && skinMenus.Remove(player.userID) && player.IsConnected)
            {
                CuiHelper.DestroyUi(player, SkinUi);
            }
        }

        // bm.ui skin <what> [value]
        internal void SkinButton(BasePlayer player, string[] args)
        {
            SkinState st;
            if (player == null || !skinMenus.TryGetValue(player.userID, out st))
            {
                CuiHelper.DestroyUi(player, SkinUi);
                return;
            }
            string what = args.Length > 1 ? args[1].ToLowerInvariant() : "close";
            if (what == "close" || !Active || !MaySkin(player, st.Target))
            {
                CloseSkinMenu(player);
                return;
            }
            int step = 0;
            if (args.Length > 2)
            {
                int.TryParse(args[2], out step);
            }
            st.Until = Time.time + 30f;
            RidableHorse horse = st.Target as RidableHorse;
            LivestockAnimal livestock = st.Target as LivestockAnimal;
            if (horse != null && what == "breed" && step >= 0 && horse.breeds != null && step < horse.breeds.Length)
            {
                horse.SetBreed(step);
                horse.SendNetworkUpdateImmediate();
            }
            else if (livestock != null)
            {
                switch (what)
                {
                    case "a":
                    case "b":
                        {
                            int[] pool = MarkerPool();
                            int count = pool.Length;
                            if (what == "a")
                            {
                                st.ColourA = (st.ColourA % count + (step < 0 ? count - 1 : 1)) % count;
                            }
                            else
                            {
                                st.ColourB = (st.ColourB % count + (step < 0 ? count - 1 : 1)) % count;
                            }
                            livestock.Genes = LivestockGenome.WithMarkers(livestock.Genes, pool[st.ColourA % count], pool[st.ColourB % count]);
                            break;
                        }
                    case "random":
                        RerollLook(livestock);
                        break;
                }
                livestock.SendNetworkUpdateImmediate();
            }
            DrawSkinMenu(player, st);
        }

        internal void SkinMenuTick()
        {
            if (skinMenus.Count == 0)
            {
                return;
            }
            foreach (KeyValuePair<ulong, SkinState> pair in skinMenus.ToList())
            {
                BasePlayer p = BasePlayer.FindByID(pair.Key);
                if (p == null || !p.IsConnected || Time.time > pair.Value.Until || !MaySkin(p, pair.Value.Target))
                {
                    if (p != null)
                    {
                        CloseSkinMenu(p);
                    }
                    else
                    {
                        skinMenus.Remove(pair.Key);
                    }
                }
            }
        }

        // Headless test: the look of a cow and of a horse is rolled again; the gameplay genes stay.
        internal void TestSpray(Action<string> reply, Vector3 pos)
        {
            var lines = new List<string>();
            Func<string, bool, bool> check = (name, ok) => { lines.Add((ok ? "PASS " : "FAIL ") + name); return ok; };
            SpeciesDef cowSp = SpeciesById("cow"), horseSp = SpeciesById("horse");
            pos.y = TerrainMeta.HeightMap.GetHeight(pos) + 0.3f;
            LivestockAnimal cow = cowSp != null ? GameManager.server.CreateEntity(cowSp.Prefab, pos, Quaternion.identity) as LivestockAnimal : null;
            RidableHorse horse = horseSp != null ? GameManager.server.CreateEntity(horseSp.Prefab, pos + new Vector3(6f, 0f, 0f), Quaternion.identity) as RidableHorse : null;
            if (cow == null || horse == null)
            {
                reply("The cow or the horse is not available.");
                return;
            }
            cow.Spawn();
            horse.Spawn();
            timer.Once(1.5f, () =>
            {
                string nameBefore = cow.AnimalName;
                int genesBefore = cow.Genes;
                int rolled = 0;
                for (int i = 0; i < 6; i++)
                {
                    RerollLook(cow);
                    rolled += (cow.AnimalName != nameBefore || cow.Genes != genesBefore) ? 1 : 0;
                }
                check("the coat changes (name and markers)", rolled > 0 && cow.AnimalName != nameBefore);
                bool same = true;
                foreach (LivestockGene g in LivestockGenome.AllGenes)
                {
                    same &= LivestockGenome.GetAllele(genesBefore, g, 0) == LivestockGenome.GetAllele(cow.Genes, g, 0) && LivestockGenome.GetAllele(genesBefore, g, 1) == LivestockGenome.GetAllele(cow.Genes, g, 1);
                }
                check("the gameplay genes are untouched", same);
                int breed = Array.IndexOf(horse.breeds, horse.GetBreed());
                RerollLook(horse);
                int breed2 = Array.IndexOf(horse.breeds, horse.GetBreed());
                check("the horse gets another breed (" + breed + " -> " + breed2 + ")", breed2 != breed && breed2 >= 0);
                cow.Kill();
                horse.Kill();
                Puts("[test] spray:\n" + string.Join("\n", lines.ToArray()));
            });
            reply("Spray test started: the result comes to the log in ~3 s.");
        }

        internal sealed class SprayModule : Module
        {
            public override int Order => 80;

            public override Dictionary<string, string> Messages()
            {
                return new Dictionary<string, string>
                {
                    ["SprayDone"] = "The spray washes the old coat away: your {0} has a new look.",
                    ["SprayNot"] = "That animal cannot be re-rolled with the spray can.",
                };
            }

            // The spray can is about to make its decal: if an animal of the player's is at that spot the look is rolled instead.
            public override object OnSprayCreate(SprayCan spray, Vector3 position, Quaternion rotation)
            {
                if (!Bm.Cfg.Features.Spray || spray == null)
                {
                    return null;
                }
                BasePlayer player = spray.GetOwnerPlayer();
                if (player == null || !Bm.Has(player, PermSpray))
                {
                    return null;
                }
                BaseEntity animal = AnimalAt(position);
                if (animal == null)
                {
                    return null;
                }
                SpeciesDef species = SpeciesOf(animal);
                if (species == null || Bm.BossOf(animal) != null || !(animal is RidableHorse || animal is LivestockAnimal))
                {
                    Bm.Tell(player, "SprayNot");
                    return true;
                }
                TamedRecord rec = Bm.RecordOf(animal);
                if (Bm.Cfg.Spray.OwnerOnly && (rec == null || !Bm.MayUse(player, rec.Owner, "team")) && !Bm.IsAdmin(player))
                {
                    Bm.Tell(player, rec == null ? "NeedTame" : "NotYours");
                    return true;
                }
                if (!Bm.RerollLook(animal))
                {
                    Bm.Tell(player, "SprayNot");
                    return true;
                }
                Bm.FormSmoke(animal.transform.position);
                Bm.Tell(player, "SprayDone", species.Display.ToLowerInvariant());
                return true;
            }

            private static BaseEntity AnimalAt(Vector3 position)
            {
                var near = new List<BaseEntity>();
                Vis.Entities(position, 1.6f, near);
                BaseEntity best = null;
                float bestDist = 0.9f;
                foreach (BaseEntity e in near)
                {
                    if (!(e is BaseNPC2 || e is RidableHorse) || e.IsDestroyed)
                    {
                        continue;
                    }
                    float d = Vector3.Distance(e.ClosestPoint(position), position);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = e;
                    }
                }
                return best;
            }
        }
    }
}

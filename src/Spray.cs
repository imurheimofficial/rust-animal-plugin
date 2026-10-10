using System;
using System.Collections.Generic;
using System.Linq;
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
                int a = LivestockGenome.RollMarker();
                int b = LivestockGenome.RollMarker();
                if (b == a)
                {
                    b = (a + 1 + UnityEngine.Random.Range(0, 15)) % 16;
                }
                livestock.Genes = LivestockGenome.WithMarkers(livestock.Genes, a, b);
                return true;
            }
            return false;
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

using System;
using System.Collections.Generic;
using System.Linq;
using Rust.Ai.Gen2;
using UnityEngine;

namespace Oxide.Plugins
{
    public partial class Beastmaster
    {
        internal enum RideTier { None, Standard, Restricted }
        internal enum TameFlow { None, Bond, Torch, Predator, SleepFeed, Admin }
        internal enum Diet { None, Herbivore, Omnivore, Carnivore, Small }

        internal sealed class SpeciesDef
        {
            public string Id;
            public string Display;
            public string Short;                    // prefab file name without extension (lower case)
            public string Prefab;                   // resolved from the game manifest at start
            public RideTier Ride;
            public bool Gear;
            public TameFlow Tame;
            public bool Transform;
            public bool TransformRestricted;
            public float Walk, Sprint, Top;         // the brief's speed table (m/s)
            public string Trait = string.Empty;
            public Diet Food;
            public bool Predator;
            public bool Livestock;
            public bool Critter;
            public bool Male;                       // ram / bull
            public bool Polar;
            public float YawOffset;                 // body yaw relative to the direction of travel
            public bool Swimmer;                    // swims with a penalty instead of wading
            public bool DrownProof;
            public bool Unavailable;                // not in this game build
            public string Note = string.Empty;

            public bool Available => !Unavailable && !string.IsNullOrEmpty(Prefab);
        }

        internal static readonly List<SpeciesDef> AllSpecies = BuildSpecies();

        private static List<SpeciesDef> BuildSpecies()
        {
            var list = new List<SpeciesDef>();
            Func<string, string, string, RideTier, bool, TameFlow, bool, float, float, float, SpeciesDef> add =
                (id, display, shortName, ride, gear, tame, transform, walk, sprint, top) =>
                {
                    var s = new SpeciesDef { Id = id, Display = display, Short = shortName, Ride = ride, Gear = gear, Tame = tame, Transform = transform, Walk = walk, Sprint = sprint, Top = top };
                    list.Add(s);
                    return s;
                };

            SpeciesDef d;
            d = add("horse", "Horse", "ridablehorse", RideTier.Standard, true, TameFlow.Bond, true, 5.5f, 14.5f, 19f);   // the horse form drives the physics vehicle
            d.Trait = "Best handling, smooth obstacle jumps."; d.Food = Diet.Herbivore; d.Livestock = true; d.Swimmer = false;
            d = add("cow", "Cow", "cow", RideTier.Standard, true, TameFlow.Bond, true, 3f, 8f, 10.5f);
            d.Trait = "Slow, heavy loot hauler."; d.Food = Diet.Herbivore; d.Livestock = true;
            d = add("bull", "Bull", "bull", RideTier.Standard, true, TameFlow.Bond, true, 4.2f, 11.5f, 13.5f);
            d.Trait = "Sprinting breaks wooden barricades."; d.Food = Diet.Herbivore; d.Livestock = true; d.Male = true;
            d = add("calf", "Calf", "calf", RideTier.None, false, TameFlow.None, true, 3f, 8f, 10.5f);
            d.Food = Diet.Herbivore; d.Livestock = true;
            d = add("sheep", "Sheep", "sheep", RideTier.Standard, true, TameFlow.Bond, true, 3f, 8f, 10.5f);
            d.Trait = "Slow, heavy loot hauler."; d.Food = Diet.Herbivore; d.Livestock = true;
            d = add("ram", "Ram", "sheep", RideTier.Standard, true, TameFlow.Bond, true, 4.2f, 11.5f, 13.5f);
            d.Trait = "Sprinting breaks wooden barricades."; d.Food = Diet.Herbivore; d.Livestock = true; d.Male = true;
            d = add("lamb", "Lamb", "lamb", RideTier.None, false, TameFlow.None, true, 3f, 8f, 10.5f);
            d.Food = Diet.Herbivore; d.Livestock = true;
            d = add("stag", "Stag", "stag", RideTier.Standard, true, TameFlow.SleepFeed, true, 5f, 14f, 17.5f);
            d.Trait = "Silent run: hoof sounds reduced by 90 %."; d.Food = Diet.Herbivore; d.Swimmer = true;
            d = add("doe", "Doe", "doe", RideTier.Standard, true, TameFlow.SleepFeed, true, 5f, 14f, 17.5f);
            d.Unavailable = true; d.Note = "No doe prefab exists in this game build; the stag covers deer."; d.Food = Diet.Herbivore;
            d = add("boar", "Boar", "boar", RideTier.Standard, true, TameFlow.Predator, true, 4f, 11f, 14.5f);
            d.Trait = "Low centre of mass: harder to hit with projectiles from the side."; d.Food = Diet.Omnivore; d.Predator = true; d.Swimmer = true;
            d = add("bear", "Brown bear", "bear", RideTier.Standard, true, TameFlow.Predator, true, 3.5f, 10.5f, 13f);
            d.Trait = "Ignores slowdown in deep water and swamp."; d.Food = Diet.Omnivore; d.Predator = true; d.Swimmer = true;
            d = add("polarbear", "Polar bear", "polarbear", RideTier.Standard, true, TameFlow.Predator, true, 3.2f, 9.5f, 12.5f);
            d.Trait = "+15 % speed on snow and ice."; d.Food = Diet.Omnivore; d.Predator = true; d.Swimmer = true; d.Polar = true;
            d = add("wolf", "Wolf", "wolf2", RideTier.Standard, true, TameFlow.Torch, true, 4.8f, 12f, 14f);
            d.Trait = "Full speed in dense forest."; d.Food = Diet.Carnivore; d.Predator = true; d.Swimmer = true;
            d = add("panther", "Panther", "panther", RideTier.Standard, true, TameFlow.Predator, true, 5.8f, 15f, 18.5f);
            d.Trait = "Jumps onto rocks 25 % higher."; d.Food = Diet.Carnivore; d.Predator = true; d.Swimmer = true;
            d = add("tiger", "Tiger", "tiger", RideTier.Standard, true, TameFlow.Predator, true, 6f, 16.5f, 21.5f);
            d.Trait = "Fastest mount, slight drift in turns."; d.Food = Diet.Carnivore; d.Predator = true; d.Swimmer = true;
            d = add("crocodile", "Crocodile", "crocodile", RideTier.Restricted, false, TameFlow.Admin, true, 3f, 7f, 9f);
            d.Trait = "Water rocket: sprint in water 18 m/s."; d.Food = Diet.Carnivore; d.Predator = true; d.Swimmer = true; d.DrownProof = true; d.TransformRestricted = true;
            d = add("chicken", "Chicken", "chicken", RideTier.Restricted, false, TameFlow.Admin, true, 1.5f, 4.5f, 6f);
            d.Trait = "Tiny hitbox."; d.Food = Diet.Small; d.Critter = true; d.TransformRestricted = true;
            d = add("rabbit", "Rabbit", "rabbit", RideTier.Restricted, false, TameFlow.Admin, true, 1.5f, 4.5f, 6f);
            d.Trait = "Tiny hitbox."; d.Food = Diet.Small; d.Critter = true; d.TransformRestricted = true;
            d = add("squirrel", "Squirrel", "squirrel", RideTier.Restricted, false, TameFlow.Admin, true, 1.5f, 4.5f, 6f);
            d.Trait = "Tiny hitbox."; d.Food = Diet.Small; d.Critter = true; d.TransformRestricted = true;
            d = add("frog", "Frog", "frog", RideTier.Restricted, false, TameFlow.Admin, true, 2.5f, 5f, 7.5f);
            d.Trait = "Super jump (jump force x3), cannot drown."; d.Food = Diet.Small; d.Critter = true; d.Swimmer = true; d.DrownProof = true; d.TransformRestricted = true;
            d = add("crab", "Crab", "crab_single", RideTier.None, false, TameFlow.None, true, 1.5f, 4.5f, 6f);
            d.Trait = "Scuttles sideways, cannot drown."; d.Food = Diet.Small; d.Critter = true; d.YawOffset = 90f; d.Swimmer = true; d.DrownProof = true;
            d = add("snake", "Snake", "snake.entity", RideTier.Restricted, false, TameFlow.Admin, false, 2f, 6f, 8f);
            d.Unavailable = true; d.TransformRestricted = true; d.Note = "In this game build the snake is a hazard entity, not a creature the plugin can control.";
            return list;
        }

        internal static SpeciesDef SpeciesById(string id)
        {
            return AllSpecies.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        }

        // The species of a live entity (ram = male sheep).
        internal static SpeciesDef SpeciesOf(BaseNetworkable entity)
        {
            if (entity == null)
            {
                return null;
            }
            if (entity is RidableHorse)
            {
                return SpeciesById("horse");
            }
            BaseEntity be = entity as BaseEntity;
            string shortName = be != null ? be.ShortPrefabName : null;
            if (string.IsNullOrEmpty(shortName))
            {
                return null;
            }
            shortName = shortName.ToLowerInvariant();
            if (shortName == "calfmale")
            {
                shortName = "calf";
            }
            if (shortName == "sheep")
            {
                LivestockAnimal livestock = entity as LivestockAnimal;
                return SpeciesById(livestock != null && livestock.IsMale ? "ram" : "sheep");
            }
            return AllSpecies.FirstOrDefault(s => s.Short == shortName);
        }

        // Matches the species to the real prefab paths of this game build.
        private void ResolveSpeciesPrefabs()
        {
            var entities = new List<string>();
            foreach (string path in GameManifest.Current.entities)
            {
                entities.Add(path.ToLowerInvariant());
            }
            foreach (SpeciesDef s in AllSpecies)
            {
                string suffix = "/" + s.Short + ".prefab";
                s.Prefab = entities.FirstOrDefault(p => p.EndsWith(suffix) && (p.Contains("rust.ai") || p.Contains("vehicles/horse") || p.Contains("agents")));
                if (s.Prefab == null)
                {
                    s.Prefab = entities.FirstOrDefault(p => p.EndsWith(suffix));
                }
                if (s.Prefab == null && !s.Unavailable)
                {
                    s.Unavailable = true;
                    s.Note = "Prefab not found in this game build.";
                    Log(1, "Species " + s.Id + ": prefab '" + s.Short + ".prefab' not found - disabled.");
                }
            }
        }

        // Speeds of a species after the config overrides.
        internal float SpeedScaleOf(SpeciesDef s)
        {
            SpeciesOverride o;
            return Cfg.Species.TryGetValue(s.Id, out o) && o.SpeedScale.HasValue ? o.SpeedScale.Value : 1f;
        }

        internal float YawOffsetOf(SpeciesDef s)
        {
            SpeciesOverride o;
            return Cfg.Species.TryGetValue(s.Id, out o) && o.YawOffset.HasValue ? o.YawOffset.Value : s.YawOffset;
        }

        // The third person camera hangs above the animal: how far above depends on how bulky the species is (the camera must not sit inside the body).
        private static readonly Dictionary<string, float> PivotFactors = new Dictionary<string, float>
        {
            { "horse", 1.18f }, { "cow", 1.45f }, { "bull", 1.4f }, { "calf", 1.35f }, { "sheep", 1.4f }, { "ram", 1.35f }, { "lamb", 1.35f },
            { "stag", 1.2f }, { "boar", 1.4f }, { "bear", 1.25f }, { "polarbear", 1.25f }, { "wolf", 1.4f }, { "panther", 1.35f }, { "tiger", 1.35f },
            { "crocodile", 2.2f }, { "chicken", 1.7f }, { "rabbit", 1.7f }, { "squirrel", 1.7f }, { "frog", 2.4f }, { "crab", 2.4f }
        };

        internal float PivotScaleOf(SpeciesDef s)
        {
            SpeciesOverride o;
            if (Cfg.Species.TryGetValue(s.Id, out o) && o.PivotScale.HasValue)
            {
                return o.PivotScale.Value;
            }
            float factor;
            return PivotFactors.TryGetValue(s.Id, out factor) ? factor : 1.25f;
        }

        // The rider's seat height of each species as found by riding them (a /bm seat up|down stored for a species wins).
        private static readonly Dictionary<string, float> SeatLifts = new Dictionary<string, float>
        {
            { "sheep", -0.30f }, { "ram", -0.30f }, { "cow", -0.20f }, { "bull", -0.20f }, { "bear", -0.25f }, { "polarbear", -0.25f },
            { "wolf", -0.40f }, { "stag", -0.35f }, { "tiger", -0.35f }, { "panther", -0.35f }, { "crocodile", -0.15f }, { "boar", -0.25f },
            { "calf", -0.35f }, { "lamb", -0.40f }
        };

        internal float SeatLiftOf(SpeciesDef s)
        {
            if (s == null)
            {
                return 0f;
            }
            SpeciesOverride o;
            if (Cfg.Species.TryGetValue(s.Id, out o) && o.SeatLift.HasValue)
            {
                return o.SeatLift.Value;
            }
            float lift;
            return SeatLifts.TryGetValue(s.Id, out lift) ? lift : 0f;
        }

        internal float SeatForwardOf(SpeciesDef s)
        {
            SpeciesOverride o;
            return s != null && Cfg.Species.TryGetValue(s.Id, out o) && o.SeatForward.HasValue ? o.SeatForward.Value : 0f;
        }

        internal bool SpeciesEnabled(SpeciesDef s)
        {
            if (s == null || !s.Available)
            {
                return false;
            }
            SpeciesOverride o;
            return !(Cfg.Species.TryGetValue(s.Id, out o) && o.Enabled.HasValue && !o.Enabled.Value);
        }
    }
}

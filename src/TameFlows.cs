using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Rust.Ai.Gen2;
using UnityEngine;

namespace Oxide.Plugins
{
    public partial class Beastmaster
    {
        internal TameFlowModule Flow;

        internal bool TrySyringe(BasePlayer player, BaseNPC2 animal)
        {
            return Flow != null && Flow.Syringe(player, animal);
        }

        internal bool TryDeerBox(BasePlayer player, BaseNPC2 animal)
        {
            return Flow != null && Flow.DeerBox(player, animal);
        }

        // E on a livestock animal that has got used to the player (the game's own familiarity) makes it theirs (R5.1-R5.3).
        internal bool TryClaim(BasePlayer player, BaseNPC2 animal, SpeciesDef species)
        {
            if (species.Tame != TameFlow.Bond || !Cfg.Features.Taming || !Has(player, PermTame))
            {
                return false;
            }
            LivestockAnimal livestock = animal as LivestockAnimal;
            if (livestock == null)
            {
                return false;
            }
            ulong best;
            float seconds = livestock.BestFamiliarity(out best);
            if (best != 0UL && best != player.userID)
            {
                Tell(player, "BondTooEarly");
                return true;
            }
            if (best == 0UL || seconds < Cfg.Taming.BondSeconds)
            {
                Tell(player, "BondProgress", Mathf.RoundToInt(seconds), Mathf.RoundToInt(Cfg.Taming.BondSeconds));
                return true;
            }
            TameAnimal(animal, player);
            return true;
        }

        // The Incapacitate Dart of the Blow Pipe: no ordinary damage, a status instead.
        internal static bool IsIncapacitateDart(HitInfo info)
        {
            BaseProjectile weapon = info.Weapon as BaseProjectile;
            if (weapon != null)
            {
                Item item = weapon.GetItem();
                if (item != null && item.info.shortname == "blowpipe")
                {
                    ItemDefinition ammo = weapon.primaryMagazine != null ? weapon.primaryMagazine.ammoType : null;
                    if (ammo != null && ammo.shortname == "dart.incapacitate")
                    {
                        return true;
                    }
                }
            }
            return info.ProjectilePrefab != null && info.ProjectilePrefab.name.IndexOf("incap", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal sealed class TameFlowModule : Module
        {
            public override int Order => 35;

            private sealed class WolfChase { public BasePlayer Player; public float Seconds; public float LastSeen; }
            private sealed class Slow { public BaseNPC2 Animal; public BasePlayer By; public float Until; public float[] Speeds; }
            private sealed class Trap { public BaseNPC2 Animal; public BasePlayer By; public int Syringes; public bool Freed; public float FreedAt; }
            private sealed class Sedated { public BaseNPC2 Animal; public BasePlayer By; public float Until; public bool Fed; public bool Eating; public float NextClip; public VirtualBox Box; }

            private readonly Dictionary<ulong, WolfChase> chases = new Dictionary<ulong, WolfChase>();
            private readonly Dictionary<ulong, Slow> slows = new Dictionary<ulong, Slow>();
            private readonly Dictionary<ulong, Trap> traps = new Dictionary<ulong, Trap>();
            private readonly Dictionary<ulong, Sedated> sedated = new Dictionary<ulong, Sedated>();

            // An animal in the middle of a taming flow (a trapped predator, a sedated deer): E has its own job there.
            public bool InFlow(BaseNetworkable animal)
            {
                if (animal == null || animal.net == null)
                {
                    return false;
                }
                Trap t;
                return sedated.ContainsKey(animal.net.ID.Value) || (traps.TryGetValue(animal.net.ID.Value, out t) && !t.Freed);
            }
            private float lastTick;

            public override void Init(Beastmaster bm)
            {
                base.Init(bm);
                bm.Flow = this;
            }

            public override Dictionary<string, string> Messages()
            {
                return new Dictionary<string, string>
                {
                    ["WolfScared"] = "The wolf is afraid of your torch. Keep chasing it with the lit torch ({0} s).",
                    ["WolfTamed"] = "The wolf stops fleeing, howls - and follows you now.",
                    ["Darted"] = "The {0} is hit: it is slowed down for {1} s.",
                    ["Sedated"] = "The {0} falls asleep for {1} s. Press E to open its inventory.",
                    ["Trapped"] = "The snap trap shuts on the {0}! Use {1} Medical Syringes on it (E).",
                    ["SyringeUsed"] = "Sedative {0}/{1}.",
                    ["PredatorCalm"] = "The {0} calms down, steps out of the trap and watches you ...",
                    ["DeerFood"] = "Put {0} {1} into the first three slots, then the saddle into the last slot.",
                    ["DeerAte"] = "It ate everything. Now put a saddle into the last slot.",
                    ["DeerTamed"] = "The {0} jumps up - fully tamed and saddled.",
                };
            }

            public override void OnDisable()
            {
                Reset();
            }

            public override void Unload()
            {
                Reset();
            }

            private void Reset()
            {
                foreach (Slow s in slows.Values.ToList())
                {
                    Unslow(s);
                }
                foreach (ulong id in chases.Keys.ToList())
                {
                    BaseNPC2 wolf = Find(id);
                    if (wolf != null)
                    {
                        Follower f = Bm.FollowerOf(wolf);
                        if (f != null && f.Reason == "scared")
                        {
                            Bm.StopFollower(wolf);
                        }
                    }
                }
                chases.Clear();
                foreach (Sedated s in sedated.Values.ToList())
                {
                    Wake(s, false);
                }
                foreach (Trap t in traps.Values.ToList())
                {
                    if (Valid(t.Animal))
                    {
                        Bm.Unpark(t.Animal, "trapped");
                    }
                }
                traps.Clear();
            }

            private static BaseNPC2 Find(ulong id)
            {
                return BaseNetworkable.serverEntities.Find(new NetworkableId(id)) as BaseNPC2;
            }

            // ---------------------------------------------------------------- darts

            public override object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
            {
                BaseNPC2 animal = entity as BaseNPC2;
                BasePlayer shooter = info != null ? info.InitiatorPlayer : null;
                if (animal == null || shooter == null || !Bm.Cfg.Features.Taming || !IsIncapacitateDart(info))
                {
                    return null;
                }
                SpeciesDef species = SpeciesOf(animal);
                if (species == null || Bm.IsTamed(animal) || Bm.DriverOfAnimal(animal) != null || !Bm.Has(shooter, PermTame))
                {
                    return null;
                }
                if (species.Tame == TameFlow.Predator)
                {
                    Slowed(animal, shooter, species);
                    return true;
                }
                if (species.Tame == TameFlow.SleepFeed)
                {
                    Sedate(animal, shooter, species);
                    return true;
                }
                return null;
            }

            private void Slowed(BaseNPC2 animal, BasePlayer by, SpeciesDef species)
            {
                RustNavMeshAgent agent = animal.GetComponent<RustNavMeshAgent>();
                if (agent == null)
                {
                    return;
                }
                ulong id = animal.net.ID.Value;
                Slow s;
                if (!slows.TryGetValue(id, out s))
                {
                    s = new Slow { Animal = animal, Speeds = new[] { agent.sneakSpeed, agent.walkSpeed, agent.jogSpeed, agent.runSpeed, agent.sprintSpeed, agent.fullSprintSpeed } };
                    slows[id] = s;
                    float k = 1f - Mathf.Clamp01(Bm.Cfg.Taming.DartSlow);
                    agent.sneakSpeed = s.Speeds[0] * k;
                    agent.walkSpeed = s.Speeds[1] * k;
                    agent.jogSpeed = s.Speeds[2] * k;
                    agent.runSpeed = s.Speeds[3] * k;
                    agent.sprintSpeed = s.Speeds[4] * k;
                    agent.fullSprintSpeed = s.Speeds[5] * k;
                }
                s.By = by;
                s.Until = Time.time + Bm.Cfg.Taming.DartSlowSeconds;
                Bm.Play(animal, "growl", "roar", "hurt");
                Bm.Tell(by, "Darted", species.Display, Mathf.RoundToInt(Bm.Cfg.Taming.DartSlowSeconds));
            }

            private void Unslow(Slow s)
            {
                ulong id = s.Animal != null && s.Animal.net != null ? s.Animal.net.ID.Value : 0UL;
                slows.Remove(id);
                if (!Valid(s.Animal))
                {
                    return;
                }
                RustNavMeshAgent agent = s.Animal.GetComponent<RustNavMeshAgent>();
                if (agent != null)
                {
                    agent.sneakSpeed = s.Speeds[0];
                    agent.walkSpeed = s.Speeds[1];
                    agent.jogSpeed = s.Speeds[2];
                    agent.runSpeed = s.Speeds[3];
                    agent.sprintSpeed = s.Speeds[4];
                    agent.fullSprintSpeed = s.Speeds[5];
                }
            }

            // ---------------------------------------------------------------- ticking

            public override void Tick(float now)
            {
                float dt = lastTick > 0f ? Mathf.Min(now - lastTick, 1f) : 0.25f;
                lastTick = now;
                if (!Bm.Cfg.Features.Taming)
                {
                    return;
                }
                TorchTick(now, dt);
                SlowTick(now);
                SedatedTick(now);
            }

            // Wolves flee from a lit torch; chased for long enough they stop, howl and follow (R5.4-R5.5).
            private void TorchTick(float now, float dt)
            {
                float range = Bm.Cfg.Taming.TorchRange;
                foreach (BasePlayer p in BasePlayer.activePlayerList)
                {
                    if (p == null || p.IsDead() || p.IsSleeping() || Bm.DriverOf(p) != null)
                    {
                        continue;
                    }
                    TorchWeapon torch = p.GetHeldEntity() as TorchWeapon;
                    if (torch == null || !torch.HasFlag(BaseEntity.Flags.On) || !Bm.Has(p, PermTame))
                    {
                        continue;
                    }
                    var near = new List<BaseNPC2>();
                    Vis.Entities(p.transform.position, range, near);
                    foreach (BaseNPC2 wolf in near)
                    {
                        SpeciesDef species = SpeciesOf(wolf);
                        if (species == null || species.Tame != TameFlow.Torch || !Alive(wolf) || Bm.IsTamed(wolf) || Bm.DriverOfAnimal(wolf) != null)
                        {
                            continue;
                        }
                        ulong id = wolf.net.ID.Value;
                        WolfChase chase;
                        if (!chases.TryGetValue(id, out chase) || (chase.Player != p && now - chase.LastSeen > 3f))
                        {
                            chase = new WolfChase { Player = p };
                            chases[id] = chase;
                            Bm.Tell(p, "WolfScared", Mathf.RoundToInt(Bm.Cfg.Taming.WolfChaseSeconds));
                        }
                        if (chase.Player != p)
                        {
                            continue;
                        }
                        chase.Seconds += dt;
                        chase.LastSeen = now;
                        Follower f = Bm.FollowerOf(wolf);
                        if (f == null || f.Reason != "scared")
                        {
                            BasePlayer scarer = p;
                            f = Bm.StartFollower(wolf, "scared", () => scarer != null && !scarer.IsDead() ? (Vector3?)scarer.transform.position : null);
                            if (f != null)
                            {
                                f.Flee = true;
                                f.FleeDistance = range * 0.7f;
                            }
                        }
                        if (chase.Seconds >= Bm.Cfg.Taming.WolfChaseSeconds)
                        {
                            chases.Remove(id);
                            Bm.StopFollower(wolf);
                            Bm.Play(wolf, "howl");
                            Bm.TameAnimal(wolf, p);
                            Bm.Tell(p, "WolfTamed");
                        }
                    }
                }
                foreach (KeyValuePair<ulong, WolfChase> pair in chases.ToList())
                {
                    if (now - pair.Value.LastSeen > 5f)
                    {
                        BaseNPC2 wolf = Find(pair.Key);
                        if (wolf != null)
                        {
                            Follower f = Bm.FollowerOf(wolf);
                            if (f != null && f.Reason == "scared")
                            {
                                Bm.StopFollower(wolf);
                            }
                        }
                        chases.Remove(pair.Key);
                    }
                }
            }

            // The dart wears off; a darted predator that steps into an armed snap trap is caught (the game's traps ignore animals).
            private void SlowTick(float now)
            {
                foreach (Slow s in slows.Values.ToList())
                {
                    if (!Valid(s.Animal) || s.Animal.IsDead() || now >= s.Until)
                    {
                        Unslow(s);
                        continue;
                    }
                    ulong id = s.Animal.net.ID.Value;
                    if (traps.ContainsKey(id))
                    {
                        continue;
                    }
                    var found = new List<BearTrap>();
                    Vis.Entities(s.Animal.transform.position, 1.6f, found);
                    foreach (BearTrap trap in found)
                    {
                        if (Valid(trap) && trap.Armed())
                        {
                            Catch(s, trap);
                            break;
                        }
                    }
                }
                foreach (KeyValuePair<ulong, Trap> pair in traps.ToList())
                {
                    Trap t = pair.Value;
                    if (!Valid(t.Animal) || t.Animal.IsDead())
                    {
                        traps.Remove(pair.Key);
                    }
                    else if (t.Freed && now >= t.FreedAt)
                    {
                        traps.Remove(pair.Key);
                        Bm.Unpark(t.Animal, "trapped");
                        if (t.By != null && t.By.IsConnected)
                        {
                            Bm.TameAnimal(t.Animal, t.By);
                        }
                    }
                }
            }

            private void Catch(Slow s, BearTrap trap)
            {
                BaseNPC2 animal = s.Animal;
                ulong id = animal.net.ID.Value;
                trap.Fire();
                animal.OnAttacked(Bm.Cfg.Taming.TrapDamage, Rust.DamageType.Stab, trap, false);
                Unslow(s);
                Bm.Park(animal, "trapped");
                traps[id] = new Trap { Animal = animal, By = s.By };
                Bm.Play(animal, "roar", "growl");
                SpeciesDef species = SpeciesOf(animal);
                Bm.Tell(s.By, "Trapped", species != null ? species.Display : "animal", Bm.Cfg.Taming.SyringesNeeded);
            }

            // E with a Medical Syringe in hand on a trapped predator (R5.9): a sedative; enough of them and it calms down.
            public bool Syringe(BasePlayer player, BaseNPC2 animal)
            {
                Trap t;
                if (!traps.TryGetValue(animal.net.ID.Value, out t) || t.Freed)
                {
                    return false;
                }
                Item active = player.GetActiveItem();
                if (active == null || active.info.shortname != "syringe.medical" || !Bm.Has(player, PermTame))
                {
                    return false;
                }
                active.UseItem(1);
                t.By = t.By ?? player;
                t.Syringes++;
                Bm.Tell(player, "SyringeUsed", t.Syringes, Bm.Cfg.Taming.SyringesNeeded);
                if (t.Syringes >= Bm.Cfg.Taming.SyringesNeeded)
                {
                    t.Freed = true;
                    t.By = player;
                    t.FreedAt = Time.time + Bm.Cfg.Taming.NeutralSeconds;
                    SpeciesDef species = SpeciesOf(animal);
                    Bm.Tell(player, "PredatorCalm", species != null ? species.Display : "animal");
                    Bm.Play(animal, "sit", "idle");
                }
                return true;
            }

            // ---------------------------------------------------------------- deer: sleep, feed, saddle (R5.11-R5.13)

            private void Sedate(BaseNPC2 animal, BasePlayer by, SpeciesDef species)
            {
                ulong id = animal.net.ID.Value;
                if (sedated.ContainsKey(id))
                {
                    return;
                }
                Bm.Park(animal, "sedated");
                var s = new Sedated { Animal = animal, By = by, Until = Time.time + Bm.Cfg.Taming.SleepSeconds };
                sedated[id] = s;
                Bm.Play(animal, "sleep", "lie");
                s.NextClip = Time.time + 5f;
                Bm.Tell(by, "Sedated", species.Display, Mathf.RoundToInt(Bm.Cfg.Taming.SleepSeconds));
            }

            private void SedatedTick(float now)
            {
                foreach (KeyValuePair<ulong, Sedated> pair in sedated.ToList())
                {
                    Sedated s = pair.Value;
                    if (!Valid(s.Animal) || s.Animal.IsDead())
                    {
                        sedated.Remove(pair.Key);
                        continue;
                    }
                    if (now >= s.Until)
                    {
                        Wake(s, true);
                        continue;
                    }
                    if (now >= s.NextClip)
                    {
                        s.NextClip = now + 6f;
                        Bm.Play(s.Animal, "sleep", "lie");     // keeps the animal lying: the parked animal has no AI to loop the clip
                    }
                }
            }

            private void Wake(Sedated s, bool announce)
            {
                sedated.Remove(s.Animal != null && s.Animal.net != null ? s.Animal.net.ID.Value : 0UL);
                if (s.Box != null)
                {
                    Bm.FinishBox(s.Box);
                }
                if (Valid(s.Animal))
                {
                    Bm.Unpark(s.Animal, "sedated");
                }
            }

            // E on the lying deer: its inventory. Three slots for food, the last one for the saddle that completes the taming.
            public bool DeerBox(BasePlayer player, BaseNPC2 animal)
            {
                Sedated s;
                if (!sedated.TryGetValue(animal.net.ID.Value, out s))
                {
                    return false;
                }
                SpeciesDef species = SpeciesOf(animal);
                string food = species != null && species.Id == "stag" ? Bm.Cfg.Taming.StagFood : Bm.Cfg.Taming.DoeFood;
                int need = Bm.Cfg.Taming.FoodNeeded;
                s.By = s.By ?? player;
                VirtualBox vb = Bm.OpenBox(player, "deer", need + 1, null, s,
                    (box, item, slot) =>
                    {
                        if (slot < need)
                        {
                            return !s.Fed && item.info.shortname == food;
                        }
                        return s.Fed && (item.info.shortname == "horse.saddle.single" || item.info.shortname == "horse.saddle.double");
                    },
                    (box, item) => DeerItemAdded(box, item, s, food, need, player),
                    box =>
                    {
                        foreach (Item rest in box.Box.inventory.itemList.ToList())
                        {
                            GiveBack(box.Player, rest);
                        }
                        s.Box = null;
                    });
                if (vb != null)
                {
                    s.Box = vb;
                    Bm.Tell(player, "DeerFood", need, ItemManager.FindItemDefinition(food) != null ? ItemManager.FindItemDefinition(food).displayName.english : food);
                }
                return true;
            }

            private void DeerItemAdded(VirtualBox box, Item item, Sedated s, string food, int need, BasePlayer player)
            {
                ItemContainer inv = box.Box.inventory;
                if (!s.Fed && !s.Eating && inv.itemList.Count(i => i.info.shortname == food && i.position < need) >= need)
                {
                    s.Eating = true;
                    Bm.timer.Once(Bm.Cfg.Taming.FoodSeconds, () =>
                    {
                        s.Eating = false;
                        if (!Valid(s.Animal) || !sedated.ContainsKey(s.Animal.net.ID.Value))
                        {
                            return;
                        }
                        foreach (Item f in inv.itemList.Where(i => i.info.shortname == food).ToList())
                        {
                            f.Remove();
                        }
                        s.Fed = true;
                        Bm.Play(s.Animal, "eat", "sleep");
                        if (player != null)
                        {
                            Bm.Tell(player, "DeerAte");
                        }
                    });
                    return;
                }
                if (s.Fed && item.position == need && (item.info.shortname == "horse.saddle.single" || item.info.shortname == "horse.saddle.double"))
                {
                    int saddle = item.info.shortname == "horse.saddle.double" ? 2 : 1;
                    item.Remove();
                    BaseNPC2 animal = s.Animal;
                    BasePlayer owner = player;
                    Bm.NextTick(() =>
                    {
                        Bm.FinishBox(box);
                        sedated.Remove(animal.net.ID.Value);
                        Bm.Unpark(animal, "sedated");
                        TamedRecord rec = Bm.TameAnimal(animal, owner);
                        if (rec != null && rec.Gear.Saddle <= 0)
                        {
                            rec.Gear.Saddle = saddle;
                        }
                        else
                        {
                            // it already carries a saddle (its wild inventory), or it could not be tamed: the saddle goes back to the player
                            Item back = ItemManager.CreateByName(saddle == 2 ? "horse.saddle.double" : "horse.saddle.single", 1);
                            if (back != null)
                            {
                                GiveBack(owner, back);
                            }
                        }
                        if (rec != null)
                        {
                            SpeciesDef species = SpeciesOf(animal);
                            Bm.Tell(owner, "DeerTamed", species != null ? species.Display : "deer");
                        }
                    });
                }
            }
        }
    }
}

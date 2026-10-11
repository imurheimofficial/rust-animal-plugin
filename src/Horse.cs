using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Oxide.Core;
using UnityEngine;

namespace Oxide.Plugins
{
    public partial class Beastmaster
    {
        // ---------------------------------------------------------------- horses (R5.1-R5.3, R8)
        // The horse is a physics vehicle with saddle seats, its own inventories and a lead, not a navigation-mesh animal. Wild horses are born
        // without a saddle (so they must be tamed first) and get a temperament; the plugin moves a horse without a rider by setting its
        // velocity (HorseDrive), and uses the game's own seats, inventories and lead for everything a player does with it.

        internal HorseModule Stable;

        private static MethodInfo FindMethod(Type type, string name, params Type[] args)
        {
            try
            {
                return type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, args, null);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static FieldInfo FindField(Type type, string name)
        {
            try
            {
                return type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static readonly MethodInfo HorseSetSeats = FindMethod(typeof(RidableHorse), "SetSeatCount", typeof(int));
        private static readonly MethodInfo HorseUpdateClients = FindMethod(typeof(RidableHorse), "UpdateClients", typeof(bool));
        private static readonly FieldInfo HorseThrottle = FindField(typeof(RidableHorse), "throttleInput");
        private static readonly FieldInfo HorseSteer = FindField(typeof(RidableHorse), "steerInput");

        private static readonly MethodInfo RpcTargetGroup = typeof(BaseEntity).Assembly.GetType("RpcTarget") != null
            ? typeof(BaseEntity).Assembly.GetType("RpcTarget").GetMethod("NetworkGroup", new[] { typeof(string) })
            : null;

        // A client call to everyone who sees the entity (no arguments), by name.
        internal static void RpcGroup(BaseEntity entity, string name)
        {
            if (RpcTargetGroup == null || ClientRpcNoArg == null || entity == null || entity.IsDestroyed)
            {
                return;
            }
            ClientRpcNoArg.Invoke(entity, new object[] { RpcTargetGroup.Invoke(null, new object[] { name }) });
        }

        internal static void SetSeats(RidableHorse horse, int seats)
        {
            if (horse != null && HorseSetSeats != null)
            {
                HorseSetSeats.Invoke(horse, new object[] { seats });
            }
        }

        internal static int SeatsOf(RidableHorse horse)
        {
            return horse.HasDoubleSaddle ? 2 : horse.HasSingleSaddle ? 1 : 0;
        }

        // The horse the player looks at.
        internal RidableHorse HorseInSight(BasePlayer player, float range, float maxAngle = 25f)
        {
            Vector3 eye = player.eyes.position;
            Vector3 dir = player.eyes.HeadForward();
            var near = new List<RidableHorse>();
            Vis.Entities(eye, range, near);
            RidableHorse best = null;
            float bestScore = float.MaxValue;
            foreach (RidableHorse h in near)
            {
                if (!Valid(h) || h.IsDead())
                {
                    continue;
                }
                Vector3 to = h.transform.position + Vector3.up * 1.2f - eye;
                float angle = Vector3.Angle(dir, to);
                if (angle > maxAngle || to.magnitude > range)
                {
                    continue;
                }
                float score = angle + to.magnitude;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = h;
                }
            }
            return best;
        }

        // ---------------------------------------------------------------- moving a horse without a rider

        // Sets the velocity of a riderless horse toward a goal; the horse's own physics (suspension, slopes, ragdoll) stay in charge of the rest.
        internal sealed class HorseDrive : FacepunchBehaviour
        {
            public RidableHorse Horse;
            public Vector3? Goal;
            public float Speed;
            public float StopDistance = 1.5f;
            public float TurnRate = 130f;
            public bool Reached;
            private Rigidbody body;
            private float nextPush;

            public bool Busy
            {
                get { return Goal.HasValue && Speed > 0.05f; }
            }

            public void MoveTo(Vector3 goal, float speed)
            {
                Goal = goal;
                Speed = speed;
                Reached = false;
            }

            public void Stop()
            {
                Goal = null;
                Speed = 0f;
            }

            private void FixedUpdate()
            {
                if (!Valid(Horse))
                {
                    Destroy(this);
                    return;
                }
                if (!Goal.HasValue || Speed <= 0.05f)
                {
                    return;
                }
                if (body == null)
                {
                    body = Horse.GetComponent<Rigidbody>();
                    if (body == null)
                    {
                        return;
                    }
                }
                if (Horse.AnyMounted() || Horse.IsLeading || Horse.HasFlag(BaseEntity.Flags.Reserved12) || body.isKinematic || body.constraints == RigidbodyConstraints.FreezeAll)
                {
                    return;
                }
                Vector3 to = Goal.Value - Horse.transform.position;
                to.y = 0f;
                float dist = to.magnitude;
                if (dist <= StopDistance)
                {
                    Goal = null;
                    Reached = true;
                    return;
                }
                float dt = Time.fixedDeltaTime;
                float want = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                float diff = Mathf.DeltaAngle(Horse.transform.eulerAngles.y, want);
                float turn = Mathf.Clamp(diff, -TurnRate * dt, TurnRate * dt);
                body.MoveRotation(Quaternion.AngleAxis(turn, Vector3.up) * body.rotation);
                float align = Mathf.Clamp01(1f - Mathf.Abs(diff) / 100f);
                float speed = Mathf.Min(Speed * align, Mathf.Max(1.2f, dist * 1.5f));
                Vector3 fwd = Horse.transform.forward;
                fwd.y = 0f;
                fwd.Normalize();
                Vector3 v = body.linearVelocity;
                body.linearVelocity = new Vector3(fwd.x * speed, v.y, fwd.z * speed);
                if (body.IsSleeping())
                {
                    body.WakeUp();
                }
                if (Time.time >= nextPush)
                {
                    nextPush = Time.time + 0.3f;
                    PushAnimation(speed, diff);
                }
            }

            // The game only tells clients about the gait of a horse that is ridden or led; this makes it do so for a free one too.
            public void PushAnimation(float speed, float steerDiff)
            {
                if (HorseUpdateClients == null || HorseThrottle == null || HorseSteer == null)
                {
                    return;
                }
                Horse.currentGait = speed < 2.3f ? RidableHorse.GaitType.Walk : speed < 5f ? RidableHorse.GaitType.Trot : speed < 8f ? RidableHorse.GaitType.Canter : RidableHorse.GaitType.Gallop;
                HorseThrottle.SetValue(Horse, 1f);
                HorseSteer.SetValue(Horse, Mathf.Clamp(steerDiff / 40f, -1f, 1f));
                HorseUpdateClients.Invoke(Horse, new object[] { true });
            }
        }

        internal sealed class HorseState
        {
            public RidableHorse Horse;
            public HorseInfo Info;
            public ulong Tag;
            public ulong NetId;
            public HorseDrive Drive;
            public Vector3 Home;
            public float ScaredUntil;
            public Vector3 ScaredFrom;
            public float SleepUntil;
            public bool SleepActive;                 // asleep (or just waking): the wake-up still has to happen
            public string Mode = "idle";             // idle, wander, flee, charge: what the brain last set the drive to
            public bool Frozen;
            public RigidbodyConstraints SavedConstraints;
            public float NextWander;
            public float NextAttack;
            public float NextDefence;
            public ulong ProvokedBy;
            public float ProvokedUntil;
            public BaseCombatEntity AttackTarget;
            public float AttackUntil;
            public float SpeedMult = 1f;             // bosses run faster
            public float Damage;                     // bosses hit harder (0 = the configured horse attack)
            public float PassUntil;                  // after a hit the charging horse runs on past its victim, then turns for the next run
            public Vector3 PassTo;
            public bool WasLeading;                  // the vanilla rope was on last tick
            public BasePlayer LastLeader;
            public float StopRequested;              // the leader asked to let go (so the rope breaking is no accident)
            public BasePlayer Tether;                // keeps following this leader on an invisible tether (the vanilla rope broke at 7 m)
            public float ChargeSince;                // an aggressive horse has been after a player since then
            public float CalmUntil;                  // after a charge an aggressive horse leaves players alone until then
            public readonly Dictionary<ulong, float> Bond = new Dictionary<ulong, float>();
            public bool Asleep(float now) { return SleepUntil > now; }
        }

        internal sealed class HorseModule : Module
        {
            public override int Order => 40;

            public readonly Dictionary<ulong, HorseState> States = new Dictionary<ulong, HorseState>();
            private float lastTick;
            private float nextJump;

            public int Count { get { return States.Count; } }

            public override void Init(Beastmaster bm)
            {
                base.Init(bm);
                bm.Stable = this;
                bm.Subscribe("OnEntitySpawned");
                bm.Subscribe("CanLootEntity");
            }

            public override Dictionary<string, string> Messages()
            {
                return new Dictionary<string, string>
                {
                    ["HorseAggressive"] = "This horse is aggressive: it will not let you near. Put it to sleep with an Incapacitate Dart first - it wakes up calm.",
                    ["HorseAsleep"] = "The horse is asleep.",
                    ["HorseWild"] = "This horse is wild. Tame it first (stay near it, then press E).",
                    ["HorseNoRide"] = "You may not ride this horse.",
                    ["HorseNoLoot"] = "You may not open the gear of this horse.",
                    ["HorseNoLead"] = "This horse cannot be led.",
                    ["HorseHurtFlee"] = "The horse bolts!",
                    ["HorseTemper"] = "This horse is {0}.",
                };
            }

            // ---------------------------------------------------------------- registry

            public HorseState StateOf(BaseNetworkable horse)
            {
                HorseState s;
                return horse != null && horse.net != null && States.TryGetValue(horse.net.ID.Value, out s) ? s : null;
            }

            private string RollTemperament()
            {
                HorseCfg h = Bm.Cfg.Horse;
                float total = Mathf.Max(0.0001f, h.CalmShare + h.ShyShare + h.AggressiveShare);
                float roll = UnityEngine.Random.value * total;
                return roll < h.CalmShare ? "calm" : roll < h.CalmShare + h.ShyShare ? "shy" : "aggressive";
            }

            public HorseState Register(RidableHorse horse, string temperament = null)
            {
                HorseState s = StateOf(horse);
                if (s != null)
                {
                    return s;
                }
                ulong tag = horse.skinID >= TagBase ? horse.skinID : Bm.NewTag();
                HorseInfo info;
                if (!Bm.Data.Horses.TryGetValue(tag, out info))
                {
                    info = new HorseInfo { Temperament = temperament ?? RollTemperament() };
                    Bm.Data.Horses[tag] = info;
                }
                horse.skinID = tag;
                s = new HorseState { Horse = horse, Info = info, Tag = tag, NetId = horse.net.ID.Value, Home = horse.transform.position, NextWander = Time.time + UnityEngine.Random.Range(8f, 30f) };
                s.Drive = horse.gameObject.AddComponent<HorseDrive>();
                s.Drive.Horse = horse;
                States[horse.net.ID.Value] = s;
                return s;
            }

            public void Forget(RidableHorse horse)
            {
                HorseState s = StateOf(horse);
                if (s == null)
                {
                    return;
                }
                States.Remove(horse.net.ID.Value);
                Wake(s, false);
                if (s.Drive != null)
                {
                    UnityEngine.Object.Destroy(s.Drive);
                }
            }

            // A wild horse is born without a saddle: it has to be tamed before a saddle can be fitted (R5.1).
            private void MakeWild(RidableHorse horse)
            {
                if (Bm.Cfg.Horse.WildUnsaddled && SeatsOf(horse) > 0 && !horse.AnyMounted())
                {
                    SetSeats(horse, 0);
                }
                Register(horse);
            }

            // A horse made by the spawn tool: wild (unsaddled, with a temperament) or tamed, with or without the saddle item.
            public void AfterSpawn(RidableHorse horse, bool tamed, bool saddled)
            {
                Register(horse);
                bool wantSaddle = saddled;
                if (SeatsOf(horse) > 0 && !wantSaddle)
                {
                    SetSeats(horse, 0);
                }
                if (wantSaddle && SeatsOf(horse) > 0 && horse.equipmentInventory != null && horse.equipmentInventory.itemList.Count == 0)
                {
                    SetSeats(horse, 0);
                    GiveSaddle(horse, 1);
                }
                if (!tamed)
                {
                    Bm.Log(3, "Horse spawned: " + StateOf(horse).Info.Temperament);
                }
            }

            public void GiveSaddle(RidableHorse horse, int seats)
            {
                Item saddle = ItemManager.CreateByName(seats > 1 ? "horse.saddle.double" : "horse.saddle.single", 1);
                if (saddle != null && !saddle.MoveToContainer(horse.equipmentInventory))
                {
                    saddle.Remove();
                }
            }

            // Only fresh, ownerless, unmarked horses of the world are made wild; the horses of vendors, of other plugins and of players stay as they are.
            public override void OnEntitySpawned(BaseNetworkable entity)
            {
                RidableHorse horse = entity as RidableHorse;
                if (horse == null || !Bm.Cfg.Features.Taming)
                {
                    return;
                }
                if (horse.skinID != 0UL || horse.OwnerID != 0UL)
                {
                    return;
                }
                Bm.timer.Once(1.5f, () =>
                {
                    if (Valid(horse) && !horse.IsForSale && horse.skinID == 0UL && horse.OwnerID == 0UL && !horse.AnyMounted() && !HasSaddleItem(horse))
                    {
                        MakeWild(horse);
                    }
                });
            }

            public override void OnServerInitialized()
            {
                int found = 0;
                var seen = new HashSet<ulong>();
                foreach (BaseNetworkable net in BaseNetworkable.serverEntities)
                {
                    RidableHorse horse = net as RidableHorse;
                    if (horse != null && horse.skinID >= TagBase && (Bm.Data.Horses.ContainsKey(horse.skinID) || Bm.Data.Tamed.ContainsKey(horse.skinID)))
                    {
                        Register(horse);
                        if (Bm.Cfg.Horse.WildUnsaddled && Bm.Cfg.Features.Taming && !Bm.Data.Tamed.ContainsKey(horse.skinID) && Bm.BossOf(horse) == null && SeatsOf(horse) > 0 && !HasSaddleItem(horse) && !horse.AnyMounted())
                        {
                            SetSeats(horse, 0);          // a reload gave it the game's saddle back; it is wild again
                        }
                        seen.Add(horse.skinID);
                        found++;
                    }
                }
                foreach (ulong tag in Bm.Data.Horses.Keys.ToList())
                {
                    if (!seen.Contains(tag))
                    {
                        Bm.Data.Horses.Remove(tag);
                    }
                }
                // the horses that were already in the world (saved by the game) are wild and unsaddled too - except a horse that carries a saddle
                // item in its equipment slot: that one belongs to a player, who fitted the saddle, and stays as it is
                foreach (BaseNetworkable net in BaseNetworkable.serverEntities.ToList())
                {
                    RidableHorse old = net as RidableHorse;
                    if (old != null && Bm.Cfg.Features.Taming && old.skinID == 0UL && old.OwnerID == 0UL && !old.IsForSale && !old.AnyMounted() && !HasSaddleItem(old))
                    {
                        MakeWild(old);
                    }
                }
                Bm.Log(3, "Horses: " + found + " registered");
            }

            public override void OnDisable()
            {
                foreach (HorseState s in States.Values)
                {
                    s.Drive.Stop();
                    Wake(s, false);
                }
            }

            public override void Unload()
            {
                foreach (HorseState s in States.Values.ToList())
                {
                    Wake(s, false);
                    if (s.Drive != null)
                    {
                        UnityEngine.Object.Destroy(s.Drive);
                    }
                    // a wild horse gets the saddle of the game back, so that nothing stays broken without the plugin
                    RidableHorse horse = s.Horse;
                    if (Valid(horse) && Bm.RecordOf(horse) == null && Bm.BossOf(horse) == null && SeatsOf(horse) == 0 && !HasSaddleItem(horse))
                    {
                        SetSeats(horse, 1);
                    }
                }
                States.Clear();
            }

            private static bool HasSaddleItem(RidableHorse horse)
            {
                if (horse.equipmentInventory == null)
                {
                    return false;
                }
                foreach (Item item in horse.equipmentInventory.itemList)
                {
                    if (horse.GetSaddleItemSeatCount(item) > 0)
                    {
                        return true;
                    }
                }
                return false;
            }

            // ---------------------------------------------------------------- the ticking brain

            public override void Tick(float now)
            {
                float dt = lastTick > 0f ? Mathf.Min(now - lastTick, 1f) : 0.25f;
                lastTick = now;
                if (States.Count == 0)
                {
                    return;
                }
                foreach (HorseState s in States.Values.ToList())
                {
                    if (!Valid(s.Horse))
                    {
                        States.Remove(s.NetId);
                        continue;
                    }
                    Guard(s, now, dt);
                }
            }

            private void Guard(HorseState s, float now, float dt)
            {
                try
                {
                    Think(s, now, dt);
                }
                catch (Exception ex)
                {
                    Bm.Log(1, "Horse brain: " + ex.Message);
                }
            }

            private void Think(HorseState s, float now, float dt)
            {
                RidableHorse h = s.Horse;
                if (Bm.DriverOfAnimal(h) != null)
                {
                    s.Drive.Stop();
                    return;                // a player is the horse
                }
                if (s.SleepActive && !s.Asleep(now))
                {
                    Wake(s, true);
                }
                if (Tether(s, now))
                {
                    return;
                }
                if (s.Asleep(now) || h.AnyMounted() || h.IsLeading || h.HasFlag(BaseEntity.Flags.Reserved12))
                {
                    return;
                }
                TamedRecord rec = Bm.RecordOf(h);
                if (rec != null)
                {
                    Duty(s, rec, now);
                    return;
                }
                if (s.AttackTarget != null && now < s.AttackUntil && Alive(s.AttackTarget))
                {
                    Charge(s, s.AttackTarget, now);
                    return;
                }
                if (!Bm.Cfg.Features.HorseAi)
                {
                    return;
                }
                Vector3 pos = h.transform.position;
                BasePlayer near = NearestPlayer(pos, Bm.Cfg.Horse.AggressionRadius + 6f);
                float nearDist = near != null ? Vector3.Distance(near.transform.position, pos) : float.MaxValue;
                string temper = s.Info.Calmed ? "calm" : s.Info.Temperament;
                // provoked by an attack: it defends itself (R19.18)
                if (s.ProvokedUntil > now)
                {
                    BasePlayer foe = BasePlayer.FindByID(s.ProvokedBy);
                    if (foe != null && !foe.IsDead())
                    {
                        Charge(s, foe, now);
                        return;
                    }
                }
                if (s.ScaredUntil > now)
                {
                    Vector3 away = pos - s.ScaredFrom;
                    away.y = 0f;
                    if (away.sqrMagnitude < 1f)
                    {
                        away = h.transform.forward;
                    }
                    Vector3 goal = pos + away.normalized * 30f;
                    s.Mode = "flee";
                    s.Drive.StopDistance = 1.5f;
                    s.Drive.MoveTo(GroundAt(goal), Bm.Cfg.Horse.FleeSpeed * s.SpeedMult);
                    return;
                }
                if (temper == "shy" && near != null && nearDist < Bm.Cfg.Horse.ShyRange)
                {
                    s.ScaredUntil = now + Bm.Cfg.Horse.ShyCalmSeconds;
                    s.ScaredFrom = near.transform.position;
                    s.Bond.Remove(near.userID);
                    RpcGroup(h, "CLIENT_Stand");
                    return;
                }
                if (s.Mode == "charge" && now < s.PassUntil)
                {
                    s.Drive.StopDistance = 1f;
                    s.Drive.MoveTo(s.PassTo, Bm.Cfg.Horse.FleeSpeed * s.SpeedMult);     // runs on past its victim after the hit
                    return;
                }
                if (temper == "aggressive" && near != null && nearDist < Bm.Cfg.Horse.AggressionRadius && !near.InSafeZone() && now >= s.CalmUntil)
                {
                    // like a ram: a short charge, then it leaves the player alone for a while (it does not hunt him across the map)
                    if (s.ChargeSince <= 0f)
                    {
                        s.ChargeSince = now;
                    }
                    if (now - s.ChargeSince > Mathf.Max(1f, Bm.Cfg.Horse.ChaseSeconds))
                    {
                        s.ChargeSince = 0f;
                        s.CalmUntil = now + Mathf.Max(0f, Bm.Cfg.Horse.CalmAfterCharge);
                        s.Drive.Stop();
                        s.Mode = "idle";
                        return;
                    }
                    Charge(s, near, now);
                    return;
                }
                s.ChargeSince = 0f;
                if (s.Mode == "flee" || s.Mode == "charge" || (s.Mode == "wander" && !s.Drive.Busy))
                {
                    s.Drive.Stop();
                    s.Mode = "idle";
                }
                Bond(s, dt);
                Wander(s, now);
            }

            // The horse code lets go of the rope at 7 m. The leader keeps the horse on an invisible tether up to Lead.BreakDistance; the rope
            // is taken again when the horse is back within reach (R6B.4).
            private bool Tether(HorseState s, float now)
            {
                RidableHorse h = s.Horse;
                if (!Bm.Cfg.Features.Lead)
                {
                    s.WasLeading = false;
                    s.Tether = null;
                    return false;
                }
                if (h.IsLeading)
                {
                    s.WasLeading = true;
                    s.LastLeader = h.leadingPlayer;
                    s.Tether = null;
                    return false;
                }
                if (s.WasLeading)
                {
                    s.WasLeading = false;
                    BasePlayer l = s.LastLeader;
                    if (l != null && l.IsConnected && !l.IsDead() && !l.isMounted && now - s.StopRequested > 3f
                        && Vector3.Distance(l.transform.position, h.transform.position) > 5f)
                    {
                        s.Tether = l;
                    }
                }
                BasePlayer leader = s.Tether;
                if (leader == null)
                {
                    return false;
                }
                float dist = leader.IsConnected ? Vector3.Distance(leader.transform.position, h.transform.position) : float.MaxValue;
                if (!leader.IsConnected || leader.IsDead() || leader.isMounted || dist > Bm.Cfg.Lead.BreakDistance || h.AnyMounted() || s.Asleep(now))
                {
                    s.Tether = null;
                    s.Drive.Stop();
                    return false;
                }
                if (dist <= 3.4f && !Bm.IsFormPlayer(leader))
                {
                    s.Tether = null;
                    s.Drive.Stop();
                    h.SetLeading(leader);
                    return true;
                }
                if (dist <= 3.4f)
                {
                    s.Mode = "tether";
                    s.Drive.Stop();                     // an animal form leads without a visible rope: the horse just stays close
                    return true;
                }
                s.Mode = "tether";
                s.Drive.StopDistance = 2.2f;
                s.Drive.MoveTo(leader.transform.position, dist < 10f ? 3.5f : dist < 25f ? 6.5f : 9f);
                return true;
            }

            private static BasePlayer NearestPlayer(Vector3 pos, float range)
            {
                BasePlayer best = null;
                float bestDist = range;
                foreach (BasePlayer p in BasePlayer.activePlayerList)
                {
                    if (p == null || p.IsDead() || p.IsSleeping() || p.IsNpc || (Instance != null && Instance.IsFormPlayer(p)) || (p.IsAdmin && p.IsFlying))
                    {
                        continue;
                    }
                    float d = Vector3.Distance(p.transform.position, pos);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = p;
                    }
                }
                return best;
            }

            private static Vector3 GroundAt(Vector3 p)
            {
                p.y = TerrainMeta.HeightMap.GetHeight(p);
                return p;
            }

            // Familiarity grows while a player stays near a calm horse and fades when they are away (R5.2).
            private void Bond(HorseState s, float dt)
            {
                if (!Bm.Cfg.Features.Taming || (s.Info.Temperament == "aggressive" && !s.Info.Calmed))
                {
                    return;
                }
                Vector3 pos = s.Horse.transform.position;
                foreach (BasePlayer p in BasePlayer.activePlayerList)
                {
                    if (p == null || p.IsDead() || p.IsSleeping() || p.IsNpc)
                    {
                        continue;
                    }
                    float have;
                    s.Bond.TryGetValue(p.userID, out have);
                    float d = Vector3.Distance(p.transform.position, pos);
                    if (d < 6f)
                    {
                        have = Mathf.Min(have + dt, 900f);
                        s.Bond[p.userID] = have;
                        // a calm horse that a player has stood next to long enough is his: no key, no saddle needed for that
                        if (have >= Mathf.Max(1f, Bm.Cfg.Horse.BondSeconds) && Bm.Has(p, PermTame) && !p.isMounted && Bm.DriverOf(p) == null
                            && s.ScaredUntil <= Time.time && !s.Asleep(Time.time) && Bm.RecordOf(s.Horse) == null)
                        {
                            if (Bm.TameAnimal(s.Horse, p) != null)
                            {
                                s.Bond.Clear();
                                Bm.TellRaw(p, "The horse trusts you now - it is yours. Put a saddle into its inventory (hold E at it) to ride it.");
                            }
                            return;
                        }
                    }
                    else if (have > 0f && d > 15f)
                    {
                        have -= dt * 0.25f;
                        if (have <= 0f) s.Bond.Remove(p.userID); else s.Bond[p.userID] = have;
                    }
                }
            }

            private void Wander(HorseState s, float now)
            {
                if (!Bm.Cfg.Horse.Wander || now < s.NextWander)
                {
                    return;
                }
                s.NextWander = now + UnityEngine.Random.Range(20f, 50f);
                Vector3 spot = s.Home + Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f) * Vector3.forward * UnityEngine.Random.Range(3f, 10f);
                spot = GroundAt(spot);
                if (WaterLevel.GetWaterDepth(spot, true, true, null) > 0.3f)
                {
                    return;
                }
                s.Mode = "wander";
                s.Drive.StopDistance = 1.5f;
                s.Drive.MoveTo(spot, 1.8f);
            }

            // ---------------------------------------------------------------- charging and striking

            // An aggressive horse attacks like a ram: it gallops straight into its victim, the impact hurts and knocks a player down, it runs on
            // past and turns for the next run. No rearing up and no screaming on the way (that looked wrong); its own hoof beats and snorts stay.
            private void Charge(HorseState s, BaseCombatEntity target, float now)
            {
                RidableHorse h = s.Horse;
                s.Mode = "charge";
                float speed = Bm.Cfg.Horse.FleeSpeed * s.SpeedMult;
                if (now < s.PassUntil)
                {
                    s.Drive.StopDistance = 1f;
                    s.Drive.MoveTo(s.PassTo, speed);
                    return;
                }
                Vector3 center;
                Vector3 nose = AttackTip(h, h.transform.eulerAngles.y, false, out center);
                Vector3 touch = target.ClosestPoint(nose);
                Vector3 ahead = touch - center;
                ahead.y = 0f;
                Vector3 fwd = h.transform.forward;
                fwd.y = 0f;
                bool inFront = ahead.sqrMagnitude < 0.01f || Vector3.Angle(fwd, ahead) < 60f;
                if (inFront && Vector3.Distance(touch, nose) <= Bm.Cfg.Combat.ChargeContact + 0.45f && now >= s.NextAttack)
                {
                    s.NextAttack = now + Bm.Cfg.Horse.AttackCooldown;
                    float damage = s.Damage > 0f ? s.Damage : Bm.Cfg.Horse.AttackDamage;
                    target.Hurt(new HitInfo(h, target, Rust.DamageType.Blunt, damage * Bm.Cfg.Combat.DamageScale, touch));
                    Bm.BumpSound(touch, "generic");
                    Bm.HitSound(target, touch);
                    Bm.BossHit(h, target);
                    BasePlayer victim = target as BasePlayer;
                    if (victim != null && !victim.IsDead() && !victim.isMounted)
                    {
                        victim.Ragdoll(fwd.normalized * 6f + Vector3.up * 3f, true, false, false, h);
                    }
                    s.PassTo = GroundAt(h.transform.position + fwd.normalized * 12f);
                    s.PassUntil = now + 1.4f;
                    s.ChargeSince = 0f;
                    s.CalmUntil = s.PassUntil + Mathf.Max(0f, Bm.Cfg.Horse.CalmAfterCharge);   // one hit, then it leaves the victim alone
                    return;
                }
                s.Drive.StopDistance = 0.2f;
                s.Drive.MoveTo(target.transform.position, speed);         // straight into it
            }

            // F as the horse: rear up and strike what is in front.
            public void Strike(HorseState s, BaseCombatEntity target, float now)
            {
                if (now < s.NextAttack)
                {
                    return;
                }
                s.NextAttack = now + Bm.Cfg.Horse.AttackCooldown;
                RidableHorse horse = s.Horse;
                Bm.timer.Once(0.1f, () =>
                {
                    if (Valid(horse) && !horse.IsDead())
                    {
                        HitZone(horse, s.Damage > 0f ? s.Damage : Bm.Cfg.Horse.AttackDamage, target, false, false);
                    }
                });
            }

            // Damage right at the nose of the horse (or right behind it for a kick): the aimed target and, for a player's horse, everything there.
            internal void HitZone(RidableHorse horse, float damage, BaseCombatEntity preferred, bool anyAnimal, bool rear)
            {
                Vector3 center;
                Vector3 tip = AttackTip(horse, horse.transform.eulerAngles.y, rear, out center);
                Vector3 facing = horse.transform.forward * (rear ? -1f : 1f);
                facing.y = 0f;
                float reach = Bm.Cfg.Combat.HitReach * ModelScale(horse) + 0.2f;
                var near = new List<BaseCombatEntity>();
                Vis.Entities(tip, reach + 2f, near);
                foreach (BaseCombatEntity e in near)
                {
                    if (e == null || e == horse || !Alive(e) || e is BaseCorpse || (e is RidableHorse && e != preferred))
                    {
                        continue;
                    }
                    BasePlayer p = e as BasePlayer;
                    if (p != null && (p.InSafeZone() || p.GetMounted() != null && HorseOfSeat(p.GetMounted()) == horse || (Instance != null && Instance.IsFormPlayer(p) && Instance.Cfg.Form.Untargetable)))
                    {
                        continue;
                    }
                    if ((e is BaseNPC2 && Bm.IsTamed(e) && e != preferred) || Bm.IsBossPair(horse, e))
                    {
                        continue;
                    }
                    if (p == null && e != preferred && !anyAnimal)
                    {
                        continue;
                    }
                    Vector3 point = e.ClosestPoint(tip);
                    Vector3 to = point - center;
                    to.y = 0f;
                    if (Vector3.Distance(point, tip) > reach || (to.sqrMagnitude > 0.01f && Vector3.Angle(facing, to) > 70f))
                    {
                        continue;
                    }
                    e.Hurt(new HitInfo(horse, e, Rust.DamageType.Blunt, damage * Bm.Cfg.Combat.DamageScale, point));
                    Bm.HitSound(e, point);
                    Bm.BossHit(horse, e);
                }
            }

            // ---------------------------------------------------------------- tamed horses: follow, wait, guard, attack

            private void Duty(HorseState s, TamedRecord rec, float now)
            {
                if (s.AttackTarget != null && now < s.AttackUntil && Alive(s.AttackTarget))
                {
                    Charge(s, s.AttackTarget, now);
                    return;
                }
                s.AttackTarget = null;
                if (rec.State == "wait" || !Bm.Cfg.Features.Taming)
                {
                    if (s.Drive.Busy) s.Drive.Stop();
                    return;
                }
                BasePlayer owner = BasePlayer.FindByID(rec.Owner);
                if (owner == null || !owner.IsConnected || owner.IsDead() || owner.IsSleeping())
                {
                    if (s.Drive.Busy) s.Drive.Stop();
                    return;
                }
                Vector3 pos = s.Horse.transform.position;
                Vector3 at = owner.transform.position;
                float dist = Vector3.Distance(pos, at);
                bool guard = rec.State == "guard";
                float stop = guard ? Bm.Cfg.Taming.GuardRadius * 0.45f : Bm.Cfg.Taming.FollowDistance;
                float tele = Bm.Cfg.Taming.TeleportDistance;
                if (tele > 0f && dist > tele)
                {
                    Teleport(s.Horse, GroundAt(at + new Vector3(2f, 0f, 2f)));
                    return;
                }
                if (dist <= stop)
                {
                    if (s.Drive.Busy) s.Drive.Stop();
                    return;
                }
                float speed = dist < 10f ? 2.2f : dist < 25f ? 5.5f : dist < 50f ? 8.5f : 11f;
                s.Drive.StopDistance = stop * 0.8f;
                s.Drive.MoveTo(at, speed);
            }

            private static void Teleport(RidableHorse horse, Vector3 spot)
            {
                Rigidbody body = horse.GetComponent<Rigidbody>();
                spot.y += 0.4f;
                if (body != null)
                {
                    body.position = spot;
                    body.linearVelocity = Vector3.zero;
                }
                horse.transform.position = spot;
                horse.SendNetworkUpdate();
            }

            public void Command(RidableHorse horse, TamedRecord rec)
            {
                HorseState s = StateOf(horse) ?? Register(horse);
                s.AttackTarget = null;
                if (rec.State == "wait")
                {
                    s.Drive.Stop();
                }
            }

            public void Attack(RidableHorse horse, BaseCombatEntity target, float seconds)
            {
                HorseState s = StateOf(horse) ?? Register(horse);
                s.AttackTarget = target;
                s.AttackUntil = Time.time + seconds;
            }

            // ---------------------------------------------------------------- taming (E on a wild horse)

            public bool Claim(BasePlayer player, HorseState s)
            {
                if (!Bm.Cfg.Features.Taming || !Bm.Has(player, PermTame))
                {
                    return false;
                }
                float now = Time.time;
                if (s.Asleep(now))
                {
                    Bm.Tell(player, "HorseAsleep");
                    return true;
                }
                if (s.Info.Temperament == "aggressive" && !s.Info.Calmed)
                {
                    Bm.Tell(player, "HorseAggressive");
                    return true;
                }
                if (s.ScaredUntil > now)
                {
                    Bm.Tell(player, "HorseHurtFlee");
                    return true;
                }
                float have;
                s.Bond.TryGetValue(player.userID, out have);
                float best = s.Bond.Count > 0 ? s.Bond.Values.Max() : 0f;
                float needed = Mathf.Max(1f, Bm.Cfg.Horse.BondSeconds);
                if (best > have + 1f && best >= needed * 0.5f)
                {
                    Bm.Tell(player, "BondTooEarly");
                    return true;
                }
                if (have < needed)
                {
                    Bm.TellRaw(player, "It is getting used to you: " + Mathf.RoundToInt(have) + " / " + Mathf.RoundToInt(needed) + " s. Stay close to it (within 6 m) and it is yours.");
                    return true;
                }
                Bm.TameAnimal(s.Horse, player);
                s.Bond.Clear();
                return true;
            }

            // ---------------------------------------------------------------- darts, defence and damage

            // ---------------------------------------------------------------- a player as the horse (horse form)

            public void TakeControl(RidableHorse horse)
            {
                HorseState s = StateOf(horse) ?? Register(horse);
                s.Drive.Stop();
                s.AttackTarget = null;
                s.Mode = "idle";
                s.ProvokedUntil = 0f;
                s.ScaredUntil = 0f;
                Wake(s, false);
                if (horse.IsLeading)
                {
                    horse.SetLeading(null);
                }
                horse.DismountAllPlayers();
                // the player is the horse: riders get the seats its saddle gives (none without one), the game keeps that up when a saddle is fitted
                SetSeats(horse, SaddleSeats(horse));
            }

            // The seats the saddle in the horse's equipment gives: 0, 1 (single) or 2 (double).
            public static int SaddleSeats(RidableHorse horse)
            {
                int seats = 0;
                if (horse != null && horse.equipmentInventory != null)
                {
                    foreach (Item it in horse.equipmentInventory.itemList)
                    {
                        seats = Mathf.Max(seats, horse.GetSaddleItemSeatCount(it));
                    }
                }
                return seats;
            }

            public void ReleaseControl(RidableHorse horse)
            {
                HorseState s = StateOf(horse);
                if (s != null)
                {
                    s.Drive.Stop();
                    s.Info.Calmed = true;
                }
                if (Valid(horse) && !horse.IsDead())
                {
                    // the seats follow the saddle again
                    horse.DismountAllPlayers();
                    SetSeats(horse, SaddleSeats(horse));
                }
            }

            // The player logged out as the horse: it stands there asleep until they return.
            public void SleepForm(RidableHorse horse)
            {
                HorseState s = StateOf(horse) ?? Register(horse);
                s.SleepUntil = float.MaxValue;
                s.SleepActive = true;
                Freeze(s);
            }

            public void WakeForm(RidableHorse horse)
            {
                HorseState s = StateOf(horse);
                if (s != null)
                {
                    s.SleepUntil = 0f;
                    Wake(s, false);
                }
            }

            // Space as the horse: the impulse over what its size allows.
            public bool JumpHorse(RidableHorse horse, float height)
            {
                Rigidbody body = horse.GetComponent<Rigidbody>();
                if (body == null || body.isKinematic || horse.HasFlag(BaseEntity.Flags.Reserved12))
                {
                    return false;
                }
                RaycastHit hit;
                if (!Physics.Raycast(horse.transform.position + Vector3.up * 0.3f, Vector3.down, out hit, 0.9f, LayerMask.GetMask("Terrain", "World", "Construction", "Deployed"), QueryTriggerInteraction.Ignore))
                {
                    return false;      // not on the ground
                }
                body.AddForce(Vector3.up * Mathf.Sqrt(2f * 9.81f * Mathf.Clamp(height, 0.3f, 2f)), ForceMode.VelocityChange);
                return true;
            }

            // F as the horse: rear up and strike what is in front.
            public void PlayerStrike(RidableHorse horse, Driver d)
            {
                if (Bm.Cfg.Horse.RearUpAttack)
                {
                    RpcGroup(horse, "CLIENT_Stand");
                }
                Bm.timer.Once(Bm.Cfg.Horse.RearUpAttack ? 0.8f : 0.1f, () =>
                {
                    if (Valid(horse) && !horse.IsDead())
                    {
                        HitZone(horse, Bm.Cfg.Horse.AttackDamage, null, true, false);
                    }
                });
            }

            // F as the horse with the camera behind it: a kick behind (the game has no kick animation for the horse - the hit and its sound).
            public void PlayerKickBack(RidableHorse horse, Driver d)
            {
                Bm.timer.Once(0.15f, () =>
                {
                    if (Valid(horse) && !horse.IsDead())
                    {
                        HitZone(horse, Bm.Cfg.Horse.AttackDamage, null, true, true);
                    }
                });
            }

            public override object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
            {
                RidableHorse horse = entity as RidableHorse;
                if (horse == null || States.Count == 0)
                {
                    return null;
                }
                if (info != null && info.damageTypes.Get(Rust.DamageType.Decay) > 0f && Bm.DriverOfAnimal(horse) != null)
                {
                    return true;           // the horse of a player does not decay while they are in it
                }
                HorseState s = StateOf(horse);
                if (s == null || info == null)
                {
                    return null;
                }
                BasePlayer by = info.InitiatorPlayer;
                if (by == null && info.Initiator != null)
                {
                    Driver attackingForm = Bm.DriverOfAnimal(info.Initiator);
                    by = attackingForm != null ? attackingForm.Player : null;       // the animal body of a player hit the horse
                }
                float now = Time.time;
                if (by != null && IsIncapacitateDart(info) && Bm.Cfg.Features.Taming)
                {
                    Sleep(s, by);
                    return true;
                }
                if (by != null && Bm.RecordOf(horse) == null && Bm.Cfg.Features.WildDefence && now >= s.NextDefence)
                {
                    // a wild horse that is attacked defends itself with the configured chance, otherwise it flees (R19.18)
                    s.NextDefence = now + 6f;
                    if (Bm.Cfg.Wild.AlwaysDefend.Contains("horse") || UnityEngine.Random.Range(0f, 100f) < Bm.Cfg.Wild.DefendChance)
                    {
                        s.ProvokedBy = by.userID;
                        s.ProvokedUntil = now + 25f;
                    }
                    else
                    {
                        s.ScaredUntil = now + Bm.Cfg.Horse.ShyCalmSeconds;
                        s.ScaredFrom = by.transform.position;
                    }
                }
                return null;
            }

            // A dart puts the horse to sleep for the configured time; it wakes up calm for good (R8.6).
            public void Sleep(HorseState s, BasePlayer by)
            {
                float now = Time.time;
                if (s.Asleep(now) || !Valid(s.Horse) || s.Horse.IsDead())
                {
                    return;
                }
                s.SleepUntil = now + Bm.Cfg.Horse.SleepSeconds;
                s.SleepActive = true;
                s.Mode = "idle";
                s.Drive.Stop();
                s.ProvokedUntil = 0f;
                s.ScaredUntil = 0f;
                s.Horse.RagdollHorse();
                RidableHorse horse = s.Horse;
                Bm.timer.Once(5.6f, () =>
                {
                    if (Valid(horse) && s.Asleep(Time.time))
                    {
                        Freeze(s);
                    }
                });
                if (by != null)
                {
                    Bm.Tell(by, "Sedated", "horse", Mathf.RoundToInt(Bm.Cfg.Horse.SleepSeconds));
                }
            }

            private void Freeze(HorseState s)
            {
                Rigidbody body = s.Horse.GetComponent<Rigidbody>();
                if (body == null || s.Frozen)
                {
                    return;
                }
                s.SavedConstraints = body.constraints;
                body.constraints = RigidbodyConstraints.FreezeAll;
                s.Frozen = true;
            }

            private void Wake(HorseState s, bool calm)
            {
                if (s.Frozen && Valid(s.Horse))
                {
                    Rigidbody body = s.Horse.GetComponent<Rigidbody>();
                    if (body != null)
                    {
                        body.constraints = s.SavedConstraints;
                        body.WakeUp();
                    }
                }
                s.Frozen = false;
                s.SleepActive = false;
                if (calm)
                {
                    s.Info.Calmed = true;
                    s.SleepUntil = 0f;
                }
            }

            // ---------------------------------------------------------------- riding rules, gear and the lead

            private static RidableHorse HorseOfSeat(BaseMountable mountable)
            {
                return mountable as RidableHorse ?? mountable.GetParentEntity() as RidableHorse;
            }

            public override object CanMountEntity(BasePlayer player, BaseMountable mountable)
            {
                if (player == null || mountable == null || States.Count == 0)
                {
                    return null;
                }
                RidableHorse horse = HorseOfSeat(mountable);
                HorseState s = horse != null ? StateOf(horse) : null;
                if (s == null)
                {
                    return null;
                }
                if (Bm.LeftOwnedBy(horse, player))
                {
                    return null;                                     // the horse the player has just left can be mounted by him
                }
                Driver formHost = Bm.DriverOfAnimal(horse);
                if (formHost != null && formHost.Kind == ControlKind.Form)
                {
                    // a player is the horse: others ride it when it has a saddle (the game gives it the seats of the saddle), never as its driver
                    return Bm.FormMountCheck(player, formHost, true) ? null : (object)false;
                }
                if (s.Asleep(Time.time) || s.Frozen)
                {
                    Bm.Tell(player, "HorseAsleep");
                    return false;
                }
                if (Bm.Cfg.Riding.AdminBypass && Bm.IsAdmin(player))
                {
                    return null;
                }
                TamedRecord rec = Bm.RecordOf(horse);
                if (!Bm.Cfg.Features.Riding || !Bm.Has(player, PermRide))
                {
                    Bm.Tell(player, "HorseNoRide");
                    return false;
                }
                if (rec == null && Bm.Cfg.Riding.RequireTame)
                {
                    Bm.Tell(player, "HorseWild");
                    return false;
                }
                if (rec != null && !Bm.MayUse(player, rec.Owner, Bm.Cfg.Riding.Ownership))
                {
                    Bm.Tell(player, "NotYours");
                    return false;
                }
                return null;
            }

            // The gear panel of a managed horse: owner and team only, never while it is wild or asleep. A horse that is a player: that player
            // (Ctrl + E) and whom the server lets handle it; the horse a player has left: that player.
            public override object CanLootEntity(BasePlayer player, BaseEntity entity)
            {
                RidableHorse horse = entity as RidableHorse;
                HorseState s = horse != null ? StateOf(horse) : null;
                if (s == null || (Bm.Cfg.Riding.AdminBypass && Bm.IsAdmin(player)))
                {
                    return null;
                }
                Driver formHost = Bm.DriverOfAnimal(horse);
                if (formHost != null && formHost.Kind == ControlKind.Form)
                {
                    if (formHost.Player == player || Bm.FormHandledBy(formHost, player))
                    {
                        return null;
                    }
                    Bm.Tell(player, "HorseNoLoot");
                    return false;
                }
                if (Bm.LeftOwnedBy(horse, player))
                {
                    return null;
                }
                TamedRecord rec = Bm.RecordOf(horse);
                if (rec == null || s.Asleep(Time.time) || !Bm.MayUse(player, rec.Owner, "team"))
                {
                    Bm.Tell(player, rec == null ? "HorseWild" : "HorseNoLoot");
                    return false;
                }
                return null;
            }

            public override object OnHorseLead(RidableHorse horse, BasePlayer player)
            {
                HorseState s = StateOf(horse);
                if (s == null)
                {
                    return null;
                }
                if (horse.IsLeading && horse.leadingPlayer == player)
                {
                    s.StopRequested = Time.time;        // this is the request to let go, not an accident
                    return null;
                }
                Driver formLed = Bm.DriverOfAnimal(horse);
                if (formLed != null && formLed.Kind == ControlKind.Form)
                {
                    return Bm.FormHandledBy(formLed, player) ? null : (object)false;       // the horse form is led by whom the server allows
                }
                TamedRecord rec = Bm.RecordOf(horse);
                if (s.Asleep(Time.time) || (rec == null && Bm.Cfg.Features.Taming) || (rec != null && !Bm.MayUse(player, rec.Owner, "team")))
                {
                    Bm.Tell(player, "HorseNoLead");
                    return false;
                }
                return null;
            }

            // A saddle in the equipment slot gives the horse its seats; taking it out takes them away again.
            public override void OnItemAddedToContainer(ItemContainer container, Item item, BasePlayer sourcePlayer)
            {
                RidableHorse horse = container != null ? container.entityOwner as RidableHorse : null;
                if (horse == null || container != horse.equipmentInventory || StateOf(horse) == null)
                {
                    return;
                }
                int seats = horse.GetSaddleItemSeatCount(item);
                if (seats > 0 && SeatsOf(horse) != seats)
                {
                    SetSeats(horse, seats);
                }
            }

            public override void OnItemRemovedFromContainer(ItemContainer container, Item item, BasePlayer sourcePlayer)
            {
                RidableHorse horse = container != null ? container.entityOwner as RidableHorse : null;
                if (horse == null || container != horse.equipmentInventory || StateOf(horse) == null)
                {
                    return;
                }
                if (horse.GetSaddleItemSeatCount(item) > 0)
                {
                    horse.DismountAllPlayers();
                    SetSeats(horse, 0);
                }
            }

            // ---------------------------------------------------------------- riding extras: jump and the rear-up attack

            public override object OnPlayerInput(BasePlayer player, InputState input)
            {
                if (States.Count == 0 || !player.isMounted)
                {
                    return null;
                }
                RidableHorse horse = player.GetMounted() != null ? HorseOfSeat(player.GetMounted()) : null;
                if (horse == null || horse.GetDriver() != player)
                {
                    return null;
                }
                HorseState s = StateOf(horse);
                if (s == null)
                {
                    return null;
                }
                if (Bm.Cfg.Horse.Jump && input.WasJustPressed(BUTTON.JUMP) && Time.time >= nextJump)
                {
                    nextJump = Time.time + Bm.Cfg.Movement.JumpCooldown;
                    Jump(horse);
                }
                return null;
            }

            private static void Jump(RidableHorse horse)
            {
                Rigidbody body = horse.GetComponent<Rigidbody>();
                if (body == null || body.isKinematic || horse.GetCurrentGaitSpeedFraction() < 0.01f && body.linearVelocity.sqrMagnitude < 1f)
                {
                    return;
                }
                RaycastHit hit;
                if (!Physics.Raycast(horse.transform.position + Vector3.up * 0.3f, Vector3.down, out hit, 0.9f, LayerMask.GetMask("Terrain", "World", "Construction"), QueryTriggerInteraction.Ignore))
                {
                    return;      // not on the ground
                }
                float h = Mathf.Clamp(Instance.Cfg.Movement.JumpHeight, 0.3f, 1.6f);
                float up = Mathf.Sqrt(2f * 9.81f * h);
                body.AddForce(Vector3.up * up, ForceMode.VelocityChange);
            }

            // F while riding a managed horse: rear up and strike forward (A14); the damage is the horse attack of the config.
            public override object OnServerCommand(BasePlayer player, ConsoleSystem.Arg arg)
            {
                if (States.Count == 0 || !player.isMounted || !Bm.Cfg.Features.Combat || arg.cmd.FullName != "inventory.lighttoggle")
                {
                    return null;
                }
                RidableHorse horse = player.GetMounted() != null ? HorseOfSeat(player.GetMounted()) : null;
                HorseState s = horse != null ? StateOf(horse) : null;
                if (s == null || horse.GetDriver() != player || !Bm.Cfg.Horse.RearUpAttack)
                {
                    return null;
                }
                float now = Time.time;
                if (now < s.NextAttack)
                {
                    return true;
                }
                s.NextAttack = now + Bm.Cfg.Horse.AttackCooldown;
                RpcGroup(horse, "CLIENT_Stand");
                Bm.timer.Once(0.8f, () =>
                {
                    if (Valid(horse) && !horse.IsDead())
                    {
                        HitZone(horse, Bm.Cfg.Horse.AttackDamage, null, true, false);
                    }
                });
                return true;
            }

            public override void OnEntityKill(BaseNetworkable entity)
            {
                RidableHorse horse = entity as RidableHorse;
                if (horse != null && States.Count > 0)
                {
                    HorseState s = StateOf(horse);
                    if (s != null)
                    {
                        States.Remove(horse.net.ID.Value);
                        if (Bm.RecordOf(horse) == null)
                        {
                            Bm.Data.Horses.Remove(s.Tag);
                        }
                    }
                }
            }

            public override void SelfTest(List<string> report)
            {
                report.Add("Horses: " + States.Count + " managed (" + States.Values.Count(x => x.Info.Temperament == "calm") + " calm, " + States.Values.Count(x => x.Info.Temperament == "shy") + " shy, " + States.Values.Count(x => x.Info.Temperament == "aggressive") + " aggressive); seat control " + (HorseSetSeats != null ? "OK" : "MISSING") + ", animation push " + (HorseUpdateClients != null && HorseThrottle != null ? "OK" : "MISSING"));
            }
        }

        // ---------------------------------------------------------------- glue for the other modules

        // E on a horse: a wild managed horse can be claimed once it knows the player.
        internal bool HorseUse(BasePlayer player, RidableHorse horse)
        {
            HorseState s = Stable != null ? Stable.StateOf(horse) : null;
            if (s == null || RecordOf(horse) != null)
            {
                return false;
            }
            return TryBossSaddle(player, horse) || Stable.Claim(player, s);
        }

        // Seconds a player has stood near a wild horse, and how many it needs (for the taming prompt). False = nothing to show.
        internal bool HorseBondOf(RidableHorse horse, BasePlayer player, out float have, out float need)
        {
            have = 0f;
            need = Mathf.Max(1f, Cfg.Horse.BondSeconds);
            HorseState s = Stable != null ? Stable.StateOf(horse) : null;
            if (s == null || RecordOf(horse) != null || !Cfg.Features.Taming || (s.Info.Temperament == "aggressive" && !s.Info.Calmed))
            {
                return false;
            }
            return s.Bond.TryGetValue(player.userID, out have) && have > 0.5f;
        }

        internal string HorseLabel(RidableHorse horse)
        {
            HorseState s = Stable != null ? Stable.StateOf(horse) : null;
            if (s == null)
            {
                return null;
            }
            TamedRecord rec = RecordOf(horse);
            return (rec != null ? "tamed" : s.Info.Calmed ? "calm" : s.Info.Temperament) + (s.Asleep(Time.time) ? ", asleep" : string.Empty);
        }

        // bm.horses list | wild | set <calm|shy|aggressive> | sleep
        private void HorseCommand(BasePlayer player, string[] args, Action<string> reply)
        {
            string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "list";
            switch (sub)
            {
                case "list":
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine("Managed horses: " + Stable.Count);
                        foreach (HorseState s in Stable.States.Values)
                        {
                            sb.AppendLine("  #" + s.Tag + " " + HorseLabel(s.Horse) + " seats " + SeatsOf(s.Horse) + " at " + s.Horse.transform.position + (RecordOf(s.Horse) != null ? " owner " + RecordOf(s.Horse).Owner : string.Empty));
                        }
                        reply(sb.ToString());
                        return;
                    }
                case "wild":
                    {
                        int n = 0;
                        foreach (BaseNetworkable net in BaseNetworkable.serverEntities.ToList())
                        {
                            RidableHorse horse = net as RidableHorse;
                            if (horse != null && horse.skinID == 0UL && horse.OwnerID == 0UL && !horse.IsForSale && !horse.AnyMounted() && Stable.StateOf(horse) == null)
                            {
                                if (Cfg.Horse.WildUnsaddled && SeatsOf(horse) > 0)
                                {
                                    SetSeats(horse, 0);
                                }
                                Stable.Register(horse);
                                n++;
                            }
                        }
                        reply(n + " idle horses are wild now (unsaddled, with a temperament).");
                        return;
                    }
                case "set":
                case "sleep":
                    {
                        if (player == null) { reply("Look at a horse in game."); return; }
                        RidableHorse horse = HorseInSight(player, 30f, 20f);
                        HorseState s = horse != null ? Stable.StateOf(horse) ?? Stable.Register(horse) : null;
                        if (s == null) { reply(L("NotLooking", null, 30)); return; }
                        if (sub == "sleep") { Stable.Sleep(s, player); return; }
                        string t = args.Length > 1 ? args[1].ToLowerInvariant() : string.Empty;
                        if (t != "calm" && t != "shy" && t != "aggressive") { reply("Usage: bm.horses set calm|shy|aggressive"); return; }
                        s.Info.Temperament = t;
                        s.Info.Calmed = false;
                        reply("This horse is " + t + " now.");
                        return;
                    }
            }
            reply("Usage: bm.horses list | wild | set calm|shy|aggressive | sleep");
        }

        // Headless test: a horse is born wild, moves, falls asleep, wakes calm, is tamed and saddled.
        internal void TestHorse(Action<string> reply, Vector3 pos)
        {
            var lines = new List<string>();
            Func<string, bool, bool> check = (name, ok) => { lines.Add((ok ? "PASS " : "FAIL ") + name); return ok; };
            SpeciesDef species = SpeciesById("horse");
            if (species == null || !species.Available)
            {
                reply("The horse is not available in this build.");
                return;
            }
            pos.y = TerrainMeta.HeightMap.GetHeight(pos) + 0.5f;
            RidableHorse horse = GameManager.server.CreateEntity(species.Prefab, pos, Quaternion.identity) as RidableHorse;
            if (horse == null)
            {
                reply("FAIL the horse could not be created");
                return;
            }
            horse.Spawn();
            check("a fresh horse is saddled by the game", SeatsOf(horse) == 1);
            float savedSleep = Cfg.Horse.SleepSeconds;
            Cfg.Horse.SleepSeconds = 4f;
            timer.Once(3.0f, () =>
            {
                HorseState s = Stable.StateOf(horse);
                check("the horse became wild (managed)", s != null);
                check("a wild horse has no saddle", SeatsOf(horse) == 0);
                check("it has a temperament (" + (s != null ? s.Info.Temperament : "?") + ")", s != null && (s.Info.Temperament == "calm" || s.Info.Temperament == "shy" || s.Info.Temperament == "aggressive"));
                if (s == null)
                {
                    Cfg.Horse.SleepSeconds = savedSleep;
                    horse.Kill();
                    Puts("[test] horse:\n" + string.Join("\n", lines.ToArray()));
                    return;
                }
                Vector3 start = horse.transform.position;
                s.Drive.StopDistance = 1.5f;
                s.Drive.MoveTo(start + horse.transform.forward * 14f, 5f);
                timer.Once(6f, () =>
                {
                    float moved = Vector3.Distance(horse.transform.position, start);
                    check("a riderless horse can be driven (moved " + moved.ToString("0.0") + " m in 6 s)", moved > 7f);
                    s.Drive.Stop();
                    Stable.Sleep(s, null);
                    timer.Once(1.0f, () =>
                    {
                        check("a dart makes the horse fall (ragdoll flag)", horse.HasFlag(BaseEntity.Flags.Reserved12) || s.Asleep(Time.time));
                        timer.Once(10f, () =>
                        {
                            check("it wakes up after the sleep (calm for good)", !s.Asleep(Time.time) && s.Info.Calmed && !s.Frozen);
                            // taming and the saddle
                            TamedRecord rec = MakeTamed(horse, species, 76561198727638665UL, false);
                            TagIndex[rec.Tag] = horse;
                            check("a tamed horse is not wild any more", RecordOf(horse) != null);
                            Item saddle = ItemManager.CreateByName("horse.saddle.single", 1);
                            bool moved2 = saddle != null && saddle.MoveToContainer(horse.equipmentInventory);
                            check("the saddle goes into the equipment slot", moved2);
                            check("the saddle gives the horse its seat", SeatsOf(horse) == 1);
                            if (saddle != null && moved2)
                            {
                                saddle.RemoveFromContainer();
                                saddle.Remove();
                            }
                            check("taking the saddle out removes the seat", SeatsOf(horse) == 0);
                            Cfg.Horse.SleepSeconds = savedSleep;
                            Data.Tamed.Remove(rec.Tag);
                            TagIndex.Remove(rec.Tag);
                            Stable.Forget(horse);
                            Data.Horses.Remove(s.Tag);
                            horse.Kill();
                            Puts("[test] horse:\n" + string.Join("\n", lines.ToArray()));
                        });
                    });
                });
            });
            reply("Horse test started: the result comes to the log in ~25 s.");
        }
    }
}

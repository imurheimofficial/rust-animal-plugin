using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    public partial class Beastmaster
    {
        // ---------------------------------------------------------------- walls, surfaces and the sounds of moving animals
        // A controlled animal is moved by the plugin, so the plugin also has to stop it at walls, tell the surface it stands on (for the sound
        // of a landing) and play the sounds of the world: the impact of a jump into a fence, a landing on grass or on a roof.

        private static readonly int ObstacleMask = LayerMask.GetMask("Construction", "Deployed", "World", "Tree", "Vehicle World");
        private static readonly int GroundProbeMask = LayerMask.GetMask("Terrain", "World", "Construction", "Deployed", "Default");

        // Keeps an animal from walking through walls: a box the size of the whole body (head and tail too) is swept along the step; the step is
        // shortened to the wall and the rest slides along it.
        internal Vector3 WallSlide(BaseCombatEntity body, Vector3 pos, Vector3 step, ref float speed, out bool hitWall, out Vector3 wallPoint, out string wallSurface)
        {
            hitWall = false;
            wallPoint = pos;
            wallSurface = "generic";
            Vector3 flat = new Vector3(step.x, 0f, step.z);
            float dist = flat.magnitude;
            if (dist < 0.0001f)
            {
                return step;
            }
            Bounds b = body.bounds;
            float k = ModelScale(body);
            float low = Mathf.Max(0.3f, 0.35f * k * Mathf.Min(1f, b.size.y));      // curbs and small stones are stepped over
            float top = Mathf.Max(low + 0.3f, b.max.y * k);
            Quaternion rot = Quaternion.Euler(0f, body.transform.eulerAngles.y, 0f);
            Vector3 half = new Vector3(Mathf.Max(0.12f, b.extents.x * k * 1.0f), Mathf.Max(0.12f, (top - low) * 0.5f), Mathf.Max(0.12f, b.extents.z * k * 1.0f + 0.08f));
            Vector3 center = pos + rot * new Vector3(b.center.x * k, 0f, b.center.z * k) + Vector3.up * ((low + top) * 0.5f);
            Vector3 dir = flat / dist;
            RaycastHit hit;
            if (!Physics.BoxCast(center, half, dir, out hit, rot, dist + 0.05f, ObstacleMask, QueryTriggerInteraction.Ignore) || hit.normal.y > 0.35f)
            {
                return step;
            }
            BaseEntity thing = hit.GetEntity();
            if (thing is BasePlayer || thing is BaseNPC2 || thing is RidableHorse || thing is BaseAnimalNPC)
            {
                return step;                            // a living thing is no wall: running into it is an attack (see ChargeTick)
            }
            hitWall = true;
            wallPoint = hit.point;
            wallSurface = ColliderSurface(hit.collider);
            float allowed = Mathf.Max(0f, hit.distance - 0.03f);
            Vector3 first = allowed < 0.04f ? Vector3.zero : dir * Mathf.Min(dist, allowed);        // no creeping along the wall: the body (and the camera) stays still
            Vector3 slide = Vector3.ProjectOnPlane(flat, hit.normal);
            slide.y = 0f;
            slide *= 0.7f;
            if (slide.sqrMagnitude > 0.000001f && Physics.BoxCast(center + first, half, slide.normalized, out hit, rot, slide.magnitude + 0.05f, ObstacleMask, QueryTriggerInteraction.Ignore) && hit.normal.y <= 0.35f)
            {
                slide = Vector3.zero;
            }
            speed = Mathf.MoveTowards(speed, 0f, Mathf.Abs(speed) * 0.5f);
            return first + slide;
        }

        // Is a part of the body inside a wall or an object when the animal stands here, turned this way? (turning checks it first)
        internal bool BodyOverlaps(BaseCombatEntity body, Vector3 pos, float yaw)
        {
            Bounds b = body.bounds;
            float k = ModelScale(body);
            float low = Mathf.Max(0.3f, 0.35f * k * Mathf.Min(1f, b.size.y));
            float top = Mathf.Max(low + 0.3f, b.max.y * k);
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 half = new Vector3(Mathf.Max(0.12f, b.extents.x * k * 0.97f), Mathf.Max(0.12f, (top - low) * 0.5f), Mathf.Max(0.12f, b.extents.z * k * 0.97f));
            Vector3 center = pos + rot * new Vector3(b.center.x * k, 0f, b.center.z * k) + Vector3.up * ((low + top) * 0.5f);
            return Physics.CheckBox(center, half, rot, ObstacleMask, QueryTriggerInteraction.Ignore);
        }

        // ---------------------------------------------------------------- surfaces

        internal static string SurfaceAt(Vector3 pos)
        {
            RaycastHit hit;
            if (Physics.Raycast(pos + Vector3.up * 0.6f, Vector3.down, out hit, 2.5f, GroundProbeMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider is TerrainCollider)
                {
                    return TerrainSurface(hit.point);
                }
                return ColliderSurface(hit.collider);
            }
            return TerrainSurface(pos);
        }

        private static string TerrainSurface(Vector3 pos)
        {
            if (WaterLevel.GetWaterDepth(pos, true, true, null) > 0.15f)
            {
                return "water";
            }
            if (TerrainMeta.SplatMap != null)
            {
                if (TerrainMeta.SplatMap.GetSplat(pos, TerrainSplat.SNOW) > 0.5f) return "snow";
                if (TerrainMeta.SplatMap.GetSplat(pos, TerrainSplat.SAND) > 0.5f) return "sand";
                if (TerrainMeta.SplatMap.GetSplat(pos, TerrainSplat.ROCK) > 0.5f) return "rock";
                if (TerrainMeta.SplatMap.GetSplat(pos, TerrainSplat.STONES) > 0.5f) return "stones";
                if (TerrainMeta.SplatMap.GetSplat(pos, TerrainSplat.GRAVEL) > 0.5f) return "gravel";
                if (TerrainMeta.SplatMap.GetSplat(pos, TerrainSplat.FOREST) > 0.5f) return "forest";
                if (TerrainMeta.SplatMap.GetSplat(pos, TerrainSplat.DIRT) > 0.5f) return "dirt";
            }
            return "grass";
        }

        internal static string ColliderSurface(Collider c)
        {
            if (c == null)
            {
                return "generic";
            }
            BuildingBlock block = c.GetComponentInParent<BuildingBlock>();
            if (block != null)
            {
                switch (block.grade)
                {
                    case BuildingGrade.Enum.Twigs:
                    case BuildingGrade.Enum.Wood: return "wood";
                    case BuildingGrade.Enum.Stone: return "rock";
                    case BuildingGrade.Enum.Metal:
                    case BuildingGrade.Enum.TopTier: return "metal";
                }
            }
            string name = (c.sharedMaterial != null ? c.sharedMaterial.name : string.Empty).ToLowerInvariant();
            if (name.Contains("wood")) return "wood";
            if (name.Contains("metal") || name.Contains("steel")) return "metal";
            if (name.Contains("rock") || name.Contains("stone")) return "rock";
            if (name.Contains("concrete") || name.Contains("brick")) return "concrete";
            if (name.Contains("glass")) return "glass";
            if (name.Contains("sand")) return "sand";
            if (name.Contains("snow")) return "snow";
            if (name.Contains("grass")) return "grass";
            if (name.Contains("dirt")) return "dirt";
            string obj = c.gameObject.name.ToLowerInvariant();
            if (obj.Contains("wood") || obj.Contains("fence") || obj.Contains("barricade")) return "wood";
            if (obj.Contains("rock") || obj.Contains("cliff") || obj.Contains("stone")) return "rock";
            if (obj.Contains("metal")) return "metal";
            return "generic";
        }

        // ---------------------------------------------------------------- sounds

        // The sound (and dust) of a landing or a push-off on a surface: the game's own jump-land effects of the barefoot player, per ground.
        internal void LandSound(Vector3 pos, string surface = null)
        {
            string s = surface ?? SurfaceAt(pos);
            string path = FindPrefabPath("jump-land/barefoot/" + s + "/") ?? FindPrefabPath("jump-land/barefoot/dirt/") ?? FindPrefabPath("jump-land/barefoot/generic/");
            RunEffect(path, pos);
        }

        // The sound of hitting a solid thing: a blunt impact of the material (wood, rock, metal, concrete ...) plus the flesh thud.
        internal void BumpSound(Vector3 pos, string surface)
        {
            string material = surface == "stones" || surface == "gravel" ? "rock" : surface;
            string path = FindPrefabPath("impacts/blunt/" + material + "/") ?? FindPrefabPath("impacts/blunt/generic/");
            RunEffect(path, pos);
            RunEffect(FindPrefabPath("impacts/blunt/flesh/"), pos);
        }

        // How high an animal can jump over or onto things: its own size times the species' jumping strength (config: Movement.JumpHeight).
        internal float JumpClearance(BaseCombatEntity body, SpeciesDef species)
        {
            float k = ModelScale(body);
            float size = Mathf.Max(0.1f, body.bounds.size.y) * k;
            float factor = 1f;
            switch (species != null ? species.Id : string.Empty)
            {
                case "stag": factor = 1.35f; break;
                case "panther": factor = 1.45f; break;
                case "tiger": factor = 1.25f; break;
                case "wolf": factor = 1.15f; break;
                case "horse": factor = 1.1f; break;
                case "bear": case "polarbear": case "boar": factor = 0.9f; break;
                case "rabbit": factor = 2f; break;
                case "squirrel": factor = 3f; break;
                case "frog": factor = 4f; break;
                case "crab": case "crocodile": factor = 0.5f; break;
            }
            return Mathf.Max(0.22f, size * 0.7f * factor * Cfg.Movement.JumpHeight);
        }

        // ---------------------------------------------------------------- running into things (R19: charges)

        private readonly Dictionary<ulong, float> chargeHits = new Dictionary<ulong, float>();

        private static bool Charges(SpeciesDef s)
        {
            if (s == null)
            {
                return false;
            }
            switch (s.Id)
            {
                case "bull": case "ram": case "cow": case "sheep": case "stag": case "boar": case "horse": return true;
                default: return false;
            }
        }

        // Bull, ram, cow, sheep, stag, boar and horse that run into a player hurt and knock them down; running into another animal hurts it
        // and counts as an attack; bulls and rams also break wooden barricades (R2.6).
        internal void ChargeTick(Driver d)
        {
            if (Time.time < nextRamCheck || !Charges(d.Species) || d.Gait < 3 || d.Speed < 2.5f)
            {
                return;
            }
            nextRamCheck = Time.time + 0.2f;
            float k = ModelScale(d.Body);
            Vector3 heading = Quaternion.Euler(0f, d.Heading, 0f) * Vector3.forward;
            Vector3 ahead = d.Body.transform.position + heading * (d.Body.bounds.extents.z * k + 0.5f) + Vector3.up * 0.9f * k;
            var found = new List<BaseEntity>();
            Vis.Entities(ahead, 1.0f * k, found);
            float now = Time.time;
            foreach (BaseEntity e in found)
            {
                if (!Valid(e) || e == d.Body || e.GetParentEntity() == d.Body)
                {
                    continue;
                }
                Barricade barricade = e as Barricade;
                if (barricade != null && (d.Species.Id == "bull" || d.Species.Id == "ram") && d.Gait >= 4 && !barricade.IsDead() && barricade.ShortPrefabName.IndexOf("wood", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    barricade.OnAttacked(60f, Rust.DamageType.Blunt, d.Body, false);
                    continue;
                }
                BaseCombatEntity target = e as BaseCombatEntity;
                if (target == null || target is BaseCorpse || !Alive(target))
                {
                    continue;
                }
                BasePlayer victim = target as BasePlayer;
                if (victim == null && !(target is BaseNPC2 || target is RidableHorse))
                {
                    continue;
                }
                if (victim != null && (victim == d.Player || victim.IsNpc && false))
                {
                    continue;
                }
                float last;
                if (chargeHits.TryGetValue(target.net.ID.Value, out last) && now - last < 2.5f)
                {
                    continue;
                }
                if (!MayHitCharge(d, target))
                {
                    continue;
                }
                chargeHits[target.net.ID.Value] = now;
                d.SuppressCrash(2f);                   // running into a living thing is an attack: the animal itself does not fall
                float damage = Cfg.Combat.ChargeDamage * ChargePower(d.Species) * Mathf.Clamp01(d.Speed / Mathf.Max(1f, d.GaitSpeed(5))) * 1.6f * k;
                if (victim != null)
                {
                    target.Hurt(new HitInfo(d.Body, target, Rust.DamageType.Blunt, damage, target.transform.position));
                    Vector3 away = (victim.transform.position - d.Body.transform.position);
                    away.y = 0f;
                    away = away.sqrMagnitude > 0.01f ? away.normalized : heading;
                    if (!victim.IsDead())
                    {
                        victim.Ragdoll(away * 7f + Vector3.up * 3.5f, true, false, false, d.Body);
                    }
                }
                else
                {
                    target.Hurt(new HitInfo(d.Body, target, Rust.DamageType.Blunt, damage * 0.8f, target.transform.position));
                }
                BumpSound(target.transform.position + Vector3.up, "generic");
                if (d.Animal != null)
                {
                    Play(d.Animal, "hurt");
                }
            }
            if (chargeHits.Count > 200)
            {
                foreach (ulong old in chargeHits.Where(kv => now - kv.Value > 10f).Select(kv => kv.Key).ToList())
                {
                    chargeHits.Remove(old);
                }
            }
        }

        private static float ChargePower(SpeciesDef s)
        {
            switch (s.Id)
            {
                case "bull": case "ram": return 1.5f;
                case "horse": case "stag": case "boar": return 1.2f;
                default: return 0.8f;
            }
        }

        private bool MayHitCharge(Driver d, BaseCombatEntity target)
        {
            BasePlayer p = target as BasePlayer;
            if (p != null)
            {
                if (!Cfg.Combat.HitPlayers || IsFormPlayer(p) || (Cfg.Combat.SparePartyMembers && SameParty(d.Player, p)))
                {
                    return false;
                }
            }
            else
            {
                if (!Cfg.Combat.HitAnimals)
                {
                    return false;
                }
                Driver other = DriverOfAnimal(target);
                if (other != null && Cfg.Combat.SparePartyMembers && SameParty(d.Player, other.Player))
                {
                    return false;
                }
                TamedRecord rec = RecordOf(target);
                if (rec != null && Cfg.Combat.SparePartyMembers && MayUse(d.Player, rec.Owner, "team"))
                {
                    return false;
                }
                if (IsBossPair(d.Body, target))
                {
                    return false;
                }
            }
            return !(Cfg.Combat.SparesSafeZone && target.InSafeZone());
        }
    }
}

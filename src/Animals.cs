using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Rust.Ai.Gen2;
using UnityEngine;

namespace Oxide.Plugins
{
    public partial class Beastmaster
    {
        private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        internal const string ParkedStateName = "BmParked";

        // ---------------------------------------------------------------- parking the AI
        // Taking over a Gen2 animal cleanly (verified in the spike): the FSM leaves its current state through the game's own exit
        // code (this stops the state's montage and clears grazing / drinking flags), sits in an empty state and stops ticking.
        // Several owners may hold the same animal; its own AI comes back when the last one lets go.

        private readonly Dictionary<ulong, HashSet<string>> parkReasons = new Dictionary<ulong, HashSet<string>>();
        private readonly Dictionary<ulong, FSMStateBase> parkReturn = new Dictionary<ulong, FSMStateBase>();

        internal bool IsParked(BaseNPC2 animal)
        {
            return animal != null && animal.net != null && parkReasons.ContainsKey(animal.net.ID.Value);
        }

        internal void Park(BaseNPC2 animal, string reason)
        {
            if (!Valid(animal))
            {
                return;
            }
            ulong id = animal.net.ID.Value;
            HashSet<string> reasons;
            if (!parkReasons.TryGetValue(id, out reasons))
            {
                reasons = new HashSet<string>();
                parkReasons[id] = reasons;
                FSMComponent fsm = animal.GetComponent<FSMComponent>();
                FSMStateBase previous = null;
                if (fsm != null)
                {
                    FSMStateBase current = fsm.CurrentState;
                    if (current == null || current.Name != ParkedStateName)
                    {
                        previous = current;
                        fsm.SetState(new State_Nothing { Name = ParkedStateName });
                    }
                    fsm.SetFsmActive(false);
                }
                parkReturn[id] = previous;
                ClearLivestockPose(animal);
                RustNavMeshAgent agent = animal.GetComponent<RustNavMeshAgent>();
                if (agent != null)
                {
                    agent.ResetPath();
                }
            }
            reasons.Add(reason);
        }

        internal void Unpark(BaseNPC2 animal, string reason)
        {
            if (animal == null || animal.net == null)
            {
                return;
            }
            ulong id = animal.net.ID.Value;
            HashSet<string> reasons;
            if (!parkReasons.TryGetValue(id, out reasons))
            {
                return;
            }
            reasons.Remove(reason);
            if (reasons.Count > 0)
            {
                return;
            }
            parkReasons.Remove(id);
            FSMStateBase back;
            parkReturn.TryGetValue(id, out back);
            parkReturn.Remove(id);
            if (animal.IsDestroyed || animal.IsDead())
            {
                return;
            }
            FSMComponent fsm = animal.GetComponent<FSMComponent>();
            if (fsm == null)
            {
                return;
            }
            FSMStateBase target = back ?? DefaultIdleState(fsm);
            fsm.SetFsmActive(true);
            if (target != null)
            {
                fsm.SetState(target);
            }
        }

        // A parked animal that dies never reaches its death state: its AI does not tick, and the one tick that would take the death transition
        // is lost when the plugin puts the AI back (entering a state resets the transition's trigger). So the AI is switched on and put straight
        // into its own dead state, with the hit that killed it: death animation, body, loot, exactly like an animal that was never held.
        internal void ReviveForDeath(BaseNPC2 animal, HitInfo info = null)
        {
            if (animal == null || animal.IsDestroyed || animal.net == null)
            {
                return;
            }
            ulong id = animal.net.ID.Value;
            Follower f = FollowerOf(animal);
            if (f != null)
            {
                Followers.Remove(id);
                f.End();
            }
            parkReasons.Remove(id);
            parkReturn.Remove(id);
            FSMComponent fsm = animal.GetComponent<FSMComponent>();
            if (fsm == null)
            {
                return;
            }
            RustNavMeshAgent agent = animal.GetComponent<RustNavMeshAgent>();
            if (agent != null)
            {
                agent.IsJumping = false;
                agent.ResetPath();
            }
            LivestockAnimal livestock = animal as LivestockAnimal;
            if (livestock != null)
            {
                ClearLivestockPose(animal);
            }
            fsm.SetFsmActive(true);
            FSMStateBase dead = FindDeadState(fsm);
            if (dead != null)
            {
                FSMPayload payload = default(FSMPayload);
                if (info != null)
                {
                    HitInfo copy = new HitInfo();
                    copy.CopyFrom(info);
                    payload.hitInfo = copy;
                }
                fsm.SetState(dead, payload);
                return;
            }
            FSMStateBase idle = DefaultIdleState(fsm);
            if (idle != null)
            {
                fsm.SetState(idle);
            }
        }

        private static FSMStateBase FindDeadState(FSMComponent fsm)
        {
            for (Type t = fsm.GetType(); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
            {
                foreach (FieldInfo f in t.GetFields(AnyInstance | BindingFlags.DeclaredOnly))
                {
                    if (typeof(State_Dead).IsAssignableFrom(f.FieldType))
                    {
                        FSMStateBase state = f.GetValue(fsm) as FSMStateBase;
                        if (state != null)
                        {
                            return state;
                        }
                    }
                }
            }
            return null;
        }

        // Plugin unload or switch-off: every animal the plugin held gets its own AI back.
        internal void ReleaseAllParked()
        {
            foreach (ulong id in parkReasons.Keys.ToList())
            {
                BaseNetworkable net = BaseNetworkable.serverEntities.Find(new NetworkableId(id));
                BaseNPC2 animal = net as BaseNPC2;
                if (animal != null)
                {
                    parkReasons[id].Clear();
                    Unpark(animal, "none");
                }
            }
            parkReasons.Clear();
            parkReturn.Clear();
        }

        internal void ForgetPark(BaseNetworkable entity)
        {
            if (entity != null && entity.net != null)
            {
                parkReasons.Remove(entity.net.ID.Value);
                parkReturn.Remove(entity.net.ID.Value);
            }
        }

        private static void ClearLivestockPose(BaseNPC2 animal)
        {
            LivestockAnimal livestock = animal as LivestockAnimal;
            if (livestock != null)
            {
                livestock.SetGrazing(false);
                livestock.SetDrinking(false);
                livestock.SetMating(false);
                livestock.SetResting(false);
                livestock.SetLyingIn(false);
                livestock.SetSleeping(false);
            }
            if (animal.HasFlag(BaseEntity.Flags.Reserved6))
            {
                animal.SetFlagLocal(BaseEntity.Flags.Reserved6, false);
                animal.SendNetworkUpdate();
            }
        }

        private static FSMStateBase DefaultIdleState(FSMComponent fsm)
        {
            var states = new Dictionary<string, FSMStateBase>();
            for (Type t = fsm.GetType(); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
            {
                foreach (FieldInfo f in t.GetFields(AnyInstance | BindingFlags.DeclaredOnly))
                {
                    if (!typeof(FSMStateBase).IsAssignableFrom(f.FieldType))
                    {
                        continue;
                    }
                    FSMStateBase state = f.GetValue(fsm) as FSMStateBase;
                    string key = f.Name.ToLowerInvariant();
                    if (state != null && !states.ContainsKey(key))
                    {
                        states[key] = state;
                    }
                }
            }
            foreach (string name in new[] { "randomidle", "idle", "roam", "wander" })
            {
                FSMStateBase state;
                if (states.TryGetValue(name, out state))
                {
                    return state;
                }
            }
            return null;
        }

        // ---------------------------------------------------------------- clips (animations with their sounds)

        internal sealed class ClipRef
        {
            public string State;
            public string Field;
            public string Name;
            public string Kind;
            public UnityEngine.Object Asset;
        }

        private readonly Dictionary<string, List<ClipRef>> clipCache = new Dictionary<string, List<ClipRef>>();

        internal static bool Has(ClipRef clip, string word)
        {
            return clip.State.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0 || clip.Name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Every AnimationClip / RootMotionData the animal's FSM states hold (one level into each state, plus arrays).
        // The list is discovered from the animal itself, so a game update that renames or adds clips is picked up.
        internal List<ClipRef> Clips(BaseNPC2 animal)
        {
            string key = animal.ShortPrefabName;
            List<ClipRef> cached;
            if (clipCache.TryGetValue(key, out cached) && cached.All(c => c.Asset != null))
            {
                return cached;
            }
            var list = new List<ClipRef>();
            FSMComponent fsm = animal.GetComponent<FSMComponent>();
            if (fsm != null)
            {
                for (Type t = fsm.GetType(); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                {
                    foreach (FieldInfo stateField in t.GetFields(AnyInstance | BindingFlags.DeclaredOnly))
                    {
                        if (!typeof(FSMStateBase).IsAssignableFrom(stateField.FieldType))
                        {
                            continue;
                        }
                        object state = stateField.GetValue(fsm);
                        if (state == null)
                        {
                            continue;
                        }
                        for (Type st = state.GetType(); st != null && st != typeof(object); st = st.BaseType)
                        {
                            foreach (FieldInfo f in st.GetFields(AnyInstance | BindingFlags.DeclaredOnly))
                            {
                                object value = f.GetValue(state);
                                AddClip(list, stateField.Name, f.Name, value);
                                var array = value as System.Collections.IEnumerable;
                                if (array != null && !(value is string) && !(value is UnityEngine.Object))
                                {
                                    int i = 0;
                                    foreach (object element in array)
                                    {
                                        AddClip(list, stateField.Name, f.Name + "[" + i++ + "]", element);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            clipCache[key] = list;
            return list;
        }

        private static void AddClip(List<ClipRef> list, string state, string field, object value)
        {
            var asset = value as UnityEngine.Object;
            if (asset == null)
            {
                return;
            }
            string typeName = asset.GetType().Name;
            if (typeName != "AnimationClip" && typeName != "RootMotionData")
            {
                return;
            }
            if (list.Any(c => c.Asset == asset))
            {
                return;
            }
            list.Add(new ClipRef { State = state, Field = field, Name = asset.name, Kind = typeName, Asset = asset });
        }

        // Plays a clip through the game's own montage channel (RootMotionPlayer -> client CL_PlayMontageDelayed).
        // RootMotionPlayer's overloads mention AnimationClip, which plugins cannot name without an extra reference,
        // so the call goes through reflection.
        internal static bool PlayClip(BaseNPC2 animal, ClipRef clip)
        {
            return PlayClipState(animal, clip) != null;
        }

        // The state of a montage that is playing (null = nothing played); it can be handed to StopClipState.
        internal static object PlayClipState(BaseNPC2 animal, ClipRef clip)
        {
            RootMotionPlayer player = animal.GetComponent<RootMotionPlayer>();
            if (player == null || clip == null || clip.Asset == null)
            {
                return null;
            }
            if (clip.Asset is RootMotionData)
            {
                // root-motion clips move the animal; play the in-place version so the controller keeps steering
                RootMotionData rm = (RootMotionData)clip.Asset;
                object inPlace = typeof(RootMotionData).GetField("inPlaceAnimation", AnyInstance)?.GetValue(rm);
                if (inPlace != null)
                {
                    return InvokeClip(player, inPlace);
                }
                MethodInfo rmMethod = typeof(RootMotionPlayer).GetMethods(AnyInstance)
                    .FirstOrDefault(m => m.Name == "PlayServerAndTakeFromPool" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(RootMotionData));
                if (rmMethod == null)
                {
                    return null;
                }
                return rmMethod.Invoke(player, new object[] { rm }) ?? new object();
            }
            return InvokeClip(player, clip.Asset);
        }

        private static object InvokeClip(RootMotionPlayer player, object animationClip)
        {
            MethodInfo method = typeof(RootMotionPlayer).GetMethods(AnyInstance)
                .FirstOrDefault(m => m.Name == "PlayServerAndTakeFromPool" && m.GetParameters().Length == 3 && m.GetParameters()[0].ParameterType == animationClip.GetType());
            if (method == null)
            {
                return null;
            }
            return method.Invoke(player, new object[] { animationClip, 1f, false }) ?? new object();
        }

        // Stops a montage that is still playing (the animal's own idle animation gives way to what the player does).
        internal static void StopClipState(BaseNPC2 animal, ref object state)
        {
            object held = state;
            state = null;
            if (held == null || animal == null || animal.IsDestroyed)
            {
                return;
            }
            RootMotionPlayer player = animal.GetComponent<RootMotionPlayer>();
            MethodInfo stop = typeof(RootMotionPlayer).GetMethod("StopServerAndReturnToPool", AnyInstance);
            if (player == null || stop == null)
            {
                return;
            }
            try
            {
                stop.Invoke(player, new object[] { held, true });
            }
            catch (Exception)
            {
            }
        }

        internal ClipRef FindClip(BaseNPC2 animal, params string[] words)
        {
            foreach (string word in words)
            {
                ClipRef clip = Clips(animal).FirstOrDefault(c => Has(c, word));
                if (clip != null)
                {
                    return clip;
                }
            }
            return null;
        }

        internal bool Play(BaseNPC2 animal, params string[] words)
        {
            ClipRef clip = FindClip(animal, words);
            return clip != null && PlayClip(animal, clip);
        }

        // ---------------------------------------------------------------- attacks of the game

        internal struct AttackParams
        {
            public float Damage;
            public float Delay;
            public Rust.DamageType Type;
            public float Range;
            public float Angle;
            public Vector2 Push;
        }

        // The game's own numbers for the attack that owns this clip (State_Attack / State_ShovingAttack fields).
        internal static AttackParams ReadAttack(BaseNPC2 animal, ClipRef clip, bool rear)
        {
            AttackParams p = new AttackParams
            {
                Damage = 20f,
                Delay = 0.5f,
                Type = Rust.DamageType.Bite,
                Range = Mathf.Max(2.2f, animal.bounds.extents.z + 1.5f),
                Angle = rear ? 180f : 60f,
                Push = Vector2.zero
            };
            FSMComponent fsm = animal.GetComponent<FSMComponent>();
            object state = null;
            for (Type t = fsm != null ? fsm.GetType() : null; t != null && state == null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(clip.State, AnyInstance | BindingFlags.DeclaredOnly);
                if (f != null)
                {
                    state = f.GetValue(fsm);
                }
            }
            if (state == null)
            {
                return p;
            }
            for (Type t = state.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (FieldInfo f in t.GetFields(AnyInstance | BindingFlags.DeclaredOnly))
                {
                    object v = f.GetValue(state);
                    switch (f.Name)
                    {
                        case "Damage": if (v is float) p.Damage = (float)v; break;
                        case "Delay": if (v is float) p.Delay = (float)v; break;
                        case "DamageType": if (v is Rust.DamageType) p.Type = (Rust.DamageType)v; break;
                        case "hitRange": if (v is float) p.Range = (float)v; break;
                        case "hitAngle": if (v is float) p.Angle = rear ? Mathf.Max(90f, (float)v) : (float)v; break;
                        case "pushForce": if (v is Vector2) p.Push = (Vector2)v; break;
                    }
                }
            }
            return p;
        }

        // ---------------------------------------------------------------- shape helpers

        // The middle of the animal's back in its local space: the highest spine bone in the middle part of the body,
        // or the bounds when the model has no spine bones.
        internal static Vector3 BackPoint(BaseNPC2 animal)
        {
            Bounds b = animal.bounds;
            Vector3 best = new Vector3(0f, b.max.y * 0.8f, b.center.z);
            bool found = false;
            if (animal.model != null && animal.model.boneTransforms != null)
            {
                foreach (Transform bone in animal.model.boneTransforms)
                {
                    if (bone == null || bone.name.IndexOf("spine", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }
                    Vector3 local = animal.transform.InverseTransformPoint(bone.position);
                    if (Mathf.Abs(local.z - b.center.z) > b.extents.z * 0.6f)
                    {
                        continue;
                    }
                    if (!found || local.y > best.y)
                    {
                        best = new Vector3(0f, local.y, local.z);
                        found = true;
                    }
                }
            }
            return best;
        }

        // The animal the player looks at (inside a cone), nearest first.
        internal BaseNPC2 AnimalInSight(BasePlayer player, float range, float maxAngle = 25f)
        {
            Vector3 eye = player.eyes.position;
            Vector3 dir = player.eyes.HeadForward();
            BaseNPC2 best = null;
            float bestScore = float.MaxValue;
            var near = new List<BaseNPC2>();
            Vis.Entities(eye, range, near);
            foreach (BaseNPC2 npc in near)
            {
                if (npc == null || npc.IsDestroyed || npc.IsDead())
                {
                    continue;
                }
                Vector3 to = npc.WorldSpaceBounds().ToBounds().center - eye;
                float dist = to.magnitude;
                if (dist > range)
                {
                    continue;
                }
                float angle = Vector3.Angle(dir, to);
                if (angle > maxAngle)
                {
                    continue;
                }
                float score = angle + dist;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = npc;
                }
            }
            return best;
        }
    }
}

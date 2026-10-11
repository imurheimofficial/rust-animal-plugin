using System;
using System.Collections.Generic;
using System.Linq;
using Rust.Ai.Gen2;
using UnityEngine;

namespace Oxide.Plugins
{
    public partial class Beastmaster
    {
        // ---------------------------------------------------------------- others handle a player in animal form (SPEC 9B)
        // Riders (after the animal has eaten food thrown in front of its head), a leash, milking and shearing, loading onto transport and breeding.
        // The animal is steered by the transformed player only: a rider is a passenger, whatever seat it sits in.

        internal readonly Dictionary<BaseMountable, Driver> GuestSeats = new Dictionary<BaseMountable, Driver>();

        // Config, the per-player refusal and the team rule decide who may handle a form.
        internal bool FormHandledBy(Driver host, BasePlayer other)
        {
            if (host == null || host.Kind != ControlKind.Form || other == null || other == host.Player || host.Player == null)
            {
                return false;
            }
            if (SettingsOf(host.Player.userID).RefuseSaddle)
            {
                return false;
            }
            string rule = (Cfg.Form.HandledBy ?? "anyone").ToLowerInvariant();
            if (rule == "nobody")
            {
                return false;
            }
            return rule != "team" || SameParty(host.Player, other);
        }

        // A player's animal carries riders when it has a saddle in its inventory: one with a single saddle, two with a double saddle (the double
        // saddle fits the horse, bull, cow, ram, sheep and stag). The horse uses the game's own saddle and seats.
        internal int MaxRiders(Driver host)
        {
            if (host == null || host.Body == null)
            {
                return 0;
            }
            // the seats of the species: two for the horse, bull and cow, one for every other animal (the player who is the animal does not sit)
            int seats = host.Species != null && DoubleSaddleSpecies(host.Species) ? 2 : 1;
            return host.Kind == ControlKind.Form ? seats : seats - 1;
        }

        // May this player climb on that animal form right now? (the horse form uses this in its vanilla mount hook, the others in TryFormHandle)
        internal bool FormMountCheck(BasePlayer player, Driver host, bool tell)
        {
            if (!FormHandledBy(host, player))
            {
                if (tell)
                {
                    TellRaw(player, "This animal does not want a rider.");
                }
                return false;
            }
            if (host.Species == null || host.Species.Critter || host.Species.Ride == RideTier.None || !host.Species.Gear)
            {
                if (tell)
                {
                    TellRaw(player, "A " + (host.Species != null ? host.Species.Display.ToLowerInvariant() : "creature") + " is too small to ride.");
                }
                return false;
            }
            if (!SaddleSupplied(player, host))
            {
                if (tell)
                {
                    TellRaw(player, "You need a saddle in your inventory to ride it (a double saddle of the animal's player also does).");
                }
                return false;
            }
            return true;
        }

        // E on somebody's animal form (the plugin's own seats for the Gen2 animals; the horse is mounted the game's way). Ctrl + E: the leash.
        internal bool TryFormHandle(BasePlayer player, Driver host, bool leash)
        {
            if (host == null || host.Kind != ControlKind.Form || !host.Alive)
            {
                return false;
            }
            if (leash)
            {
                ToggleLeash(player, host);
                return true;
            }
            if (host.Horse != null)
            {
                return false;                     // the vanilla mount of the horse takes it from here
            }
            host.PruneGuests();
            if (!FormMountCheck(player, host, true))
            {
                return true;
            }
            if (host.Guests.Count >= MaxRiders(host))
            {
                TellRaw(player, "There is no room left on the " + host.Species.Display.ToLowerInvariant() + ".");
                return true;
            }
            host.AddGuest(player);
            return true;
        }

        // ---------------------------------------------------------------- the leash

        internal BasePlayer LeaderOf(Driver d)
        {
            if (d == null)
            {
                return null;
            }
            if (d.Leader != null && d.Leader.IsConnected && !d.Leader.IsDead())
            {
                return d.Leader;
            }
            LivestockAnimal ls = d.Animal as LivestockAnimal;
            if (ls != null && ls.IsLeading())
            {
                BasePlayer vanilla = ls.LeadingPlayer.Get(true);
                if (vanilla != null && !vanilla.IsDead())
                {
                    return vanilla;
                }
            }
            if (d.Horse != null && d.Horse.IsLeading && d.Horse.leadingPlayer != null)
            {
                return d.Horse.leadingPlayer;
            }
            return null;
        }

        private void ToggleLeash(BasePlayer player, Driver host)
        {
            if (host.Leader == player)
            {
                host.Leader = null;
                TellRaw(player, "You let the " + host.Species.Display.ToLowerInvariant() + " go.");
                TellRaw(host.Player, "You are free of the leash.");
                return;
            }
            if (!FormHandledBy(host, player))
            {
                TellRaw(player, "This animal does not want to be led.");
                return;
            }
            if (host.Leader != null)
            {
                TellRaw(player, "Somebody else leads it already.");
                return;
            }
            host.Leader = player;
            TellRaw(player, "You lead the " + host.Species.Display.ToLowerInvariant() + ". Hold Ctrl and press E at it again to let it go.");
            TellRaw(host.Player, player.displayName + " leads you on a leash.");
        }

        // A taut leash: the animal cannot go further than the rope allows and is drawn along when the leader walks away.
        internal Vector3 LeashClamp(Driver d, Vector3 next)
        {
            BasePlayer leader = LeaderOf(d);
            if (leader == null)
            {
                return next;
            }
            Vector3 at = leader.transform.position;
            Vector3 to = at - next;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist > Cfg.Lead.BreakDistance)
            {
                if (d.Leader == leader)
                {
                    d.Leader = null;
                    TellRaw(leader, "The leash broke.");
                    TellRaw(d.Player, "The leash broke.");
                }
                return next;
            }
            float length = Mathf.Max(2f, Cfg.Form.LeashLength);
            if (dist <= length)
            {
                return next;
            }
            Vector3 clamped = next + to.normalized * (dist - length);
            clamped.y = next.y;
            return clamped;
        }

        // The horse: its speed is set toward the leader when the rope is taut.
        internal void LeashHorse(Driver d, Rigidbody body)
        {
            BasePlayer leader = LeaderOf(d);
            if (leader == null || body == null)
            {
                return;
            }
            Vector3 to = leader.transform.position - body.position;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist > Cfg.Lead.BreakDistance)
            {
                if (d.Leader == leader)
                {
                    d.Leader = null;
                }
                return;
            }
            if (dist > Mathf.Max(2f, Cfg.Form.LeashLength))
            {
                Vector3 v = body.linearVelocity;
                Vector3 pull = to.normalized * Mathf.Clamp(dist - Cfg.Form.LeashLength, 1.5f, 7f);
                body.linearVelocity = new Vector3(pull.x, v.y, pull.z);
            }
        }

        // ---------------------------------------------------------------- milking, shearing, leading by the game's own interactions

        // The game's livestock only accepts a hand that it trusts. A player next to a form of cow, bull, sheep or ram is a familiar one, as far as
        // the rules of the server allow it, so that the game's own milking, shearing, rope and loading work on the transformed player.
        internal void CreditTrust(Driver d)
        {
            LivestockAnimal ls = d.Animal as LivestockAnimal;
            if (ls == null || d.Kind != ControlKind.Form)
            {
                return;
            }
            float need = ConVar.Livestock.trustToLead + 1f;
            foreach (BasePlayer p in BasePlayer.activePlayerList)
            {
                if (p == null || p.IsNpc || p.IsDead() || p.IsSleeping() || p == d.Player || Vector3.Distance(p.transform.position, ls.transform.position) > 10f)
                {
                    continue;
                }
                if (!FormHandledBy(d, p))
                {
                    continue;
                }
                float have = ls.TrustOf(p);
                if (have < need)
                {
                    ls.AddFamiliarity(p.userID, need - have);
                }
            }
        }

        // ---------------------------------------------------------------- breeding of forms (R19.7)

        // /bm breed: a cow form and a bull (a ram and a sheep) next to each other: the vanilla pregnancy starts and a calf (a lamb) is born in time.
        internal void BreedCommand(BasePlayer player, Action<string> reply)
        {
            Driver d = DriverOf(player);
            LivestockAnimal me = d != null && d.Kind == ControlKind.Form ? d.Animal as LivestockAnimal : null;
            if (me == null)
            {
                reply("Only a cow, bull, sheep or ram form can breed.");
                return;
            }
            SpeciesDef mine = d.Species;
            if (mine.Id != "cow" && mine.Id != "bull" && mine.Id != "sheep" && mine.Id != "ram")
            {
                reply("A " + mine.Display.ToLowerInvariant() + " cannot breed.");
                return;
            }
            string wanted = mine.Id == "cow" ? "bull" : mine.Id == "bull" ? "cow" : mine.Id == "sheep" ? "ram" : "sheep";
            var near = new List<BaseNPC2>();
            Vis.Entities(me.transform.position, 7f, near);
            LivestockAnimal partner = null;
            float best = float.MaxValue;
            foreach (BaseNPC2 npc in near)
            {
                LivestockAnimal other = npc as LivestockAnimal;
                SpeciesDef sp = other != null ? SpeciesOf(other) : null;
                if (other == null || other == me || sp == null || sp.Id != wanted || other.IsDead())
                {
                    continue;
                }
                float dist = Vector3.Distance(other.transform.position, me.transform.position);
                if (dist < best)
                {
                    best = dist;
                    partner = other;
                }
            }
            if (partner == null)
            {
                reply("There is no " + wanted + " within 7 m.");
                return;
            }
            LivestockAnimal female = me.IsMale ? partner : me;
            LivestockAnimal male = me.IsMale ? me : partner;
            if (!female.CanBePregnant() || !male.CanBreed())
            {
                reply("They are not ready: both have to be adults and not on a breeding cooldown.");
                return;
            }
            if (!female.MakePregnant(male))
            {
                reply("Nothing happened.");
                return;
            }
            RunEffect(FindPrefabPath("fx", "heart") ?? FindPrefabPath("fx", "sparkle"), female.transform.position + Vector3.up * 1.5f);
            reply(female == me ? "You are pregnant now. A young one will be born in time." : "She is pregnant now. A young one will be born in time.");
            Driver other2 = DriverOfAnimal(partner);
            if (other2 != null)
            {
                TellRaw(other2.Player, female == partner ? "You are pregnant now. A young one will be born in time." : "Your mate is pregnant now.");
            }
        }

        // ---------------------------------------------------------------- the module

        internal sealed class GuestModule : Module
        {
            public override int Order => 14;

            private float nextTrust;

            public override void Tick(float now)
            {
                if (Bm.Drivers.Count == 0)
                {
                    return;
                }
                foreach (Driver d in Bm.Drivers.Values.ToList())
                {
                    if (d == null || !d.Alive || d.Kind != ControlKind.Form)
                    {
                        continue;
                    }
                    d.PruneGuests();
                    if (d.Leader != null && (!d.Leader.IsConnected || d.Leader.IsDead()))
                    {
                        d.Leader = null;
                    }
                }
                if (now < nextTrust)
                {
                    return;
                }
                nextTrust = now + 2f;
                foreach (Driver d in Bm.Drivers.Values.ToList())
                {
                    if (d != null && d.Alive)
                    {
                        Bm.CreditTrust(d);
                    }
                }
            }

            // A rider on an animal form is carried; it never steers (the buttons that would drive a horse are dropped from its input).
            public override object OnPlayerInput(BasePlayer player, InputState input)
            {
                if (Bm.GuestSeats.Count == 0 && !player.isMounted)
                {
                    return null;
                }
                BaseMountable seat = player.GetMounted();
                if (seat == null)
                {
                    return null;
                }
                Driver host = HostOfSeat(seat);
                if (host != null && host.Player != player)
                {
                    input.current.buttons &= ~((int)BUTTON.FORWARD | (int)BUTTON.BACKWARD | (int)BUTTON.LEFT | (int)BUTTON.RIGHT | (int)BUTTON.SPRINT);
                }
                return null;
            }

            private Driver HostOfSeat(BaseMountable seat)
            {
                Driver host;
                if (Bm.GuestSeats.TryGetValue(seat, out host))
                {
                    return host;
                }
                RidableHorse horse = seat as RidableHorse ?? seat.GetParentEntity() as RidableHorse;
                Driver d = horse != null ? Bm.DriverOfAnimal(horse) : null;
                return d != null && d.Kind == ControlKind.Form ? d : null;
            }

            public override void OnEntityDismounted(BaseMountable mountable, BasePlayer player)
            {
                Driver host;
                if (mountable != null && Bm.GuestSeats.TryGetValue(mountable, out host))
                {
                    BaseMountable seat = mountable;
                    Bm.NextTick(() => host.DropGuestSeat(seat));
                }
            }

            public override void SelfTest(List<string> report)
            {
                report.Add("Guests: " + Bm.GuestSeats.Count + " passenger seats on animal forms");
            }
        }
    }
}

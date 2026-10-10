using System.Collections.Generic;

namespace Oxide.Plugins
{
    public partial class Beastmaster
    {
        // All player-facing text is English and goes through the Oxide lang system (oxide/lang/en/Beastmaster.json),
        // so a server owner can reword it without touching the code.
        private void RegisterMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoPermission"] = "You do not have permission for that.",
                ["PluginOff"] = "Beastmaster is switched off on this server.",
                ["Enabled"] = "Beastmaster is now ON.",
                ["Disabled"] = "Beastmaster is now OFF. Everyone was released.",
                ["NotLooking"] = "Look at an animal first (within {0} m).",
                ["UnknownSpecies"] = "Unknown species '{0}'. Known: {1}",
                ["SpeciesOff"] = "{0} is not available (disabled or not in this game build).",
                ["NeedTame"] = "This animal is not tamed. Tame it first (see Help - Taming).",
                ["NeedSaddle"] = "This animal has no saddle. Open its gear with E and fit one.",
                ["NotYours"] = "This animal belongs to someone else.",
                ["AlreadyControlling"] = "You are already riding or in animal form. Press X first.",
                ["Riding"] = "Riding the {0}. W/A/S/D move, Shift faster, Ctrl brake, Space jump, F attack, G view, X get off.",
                ["Form"] = "You are now a {0}. W/A/S/D move, Shift faster, Ctrl brake, Space jump, F attack, E eat / use, R skin, G night vision, X back to human.",
                ["Released"] = "You are free again.",
                ["ViewThird"] = "View: third person. Move the mouse to look around; G or R changes the view.",
                ["ViewFirst"] = "View: first person. G or R changes the view.",
                ["Brake"] = "Brake.",
                ["Stop"] = "Stop.",
                ["NoAttack"] = "This animal has no attack.",
                ["HitSummary"] = "Hit: {0}",
                ["Died"] = "Your animal died - and you with it.",
                ["Spawned"] = "Spawned {0}x {1}.",
                ["SpawnLimit"] = "Spawn limit reached ({0}).",
                ["SpawnCooldown"] = "Wait a moment before spawning again.",
                ["GrantOk"] = "Menu access granted to {0}.",
                ["RevokeOk"] = "Menu access removed from {0}.",
                ["PlayerNotFound"] = "Player '{0}' not found.",
                ["NoAnimalsNearby"] = "No animal of yours nearby.",
                ["CommandSet"] = "{0}: {1}.",
                ["Cleared"] = "Removed {0} plugin-spawned animals.",
                ["Reloaded"] = "Config reloaded.",
                ["RideHorseVanilla"] = "A horse is ridden with E on its own saddle.",
            }, this);
        }
    }
}

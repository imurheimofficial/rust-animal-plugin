using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("DevSpawn", "Beastmaster dev", "0.1.0")]
    [Description("DEV ONLY - never shipped. The 1500 m test map has no beach spawn points: the game then spawns players at (0, 0, 0), under the ground, and antihack kills + kicks them (InsideTerrain). This hands out a spot on open, dry, flat land instead.")]
    public class DevSpawn : RustPlugin
    {
        private const float MonumentGap = 60f;
        private const float MaxSlope = 20f;
        private const float Step = 12f;

        private readonly List<Vector3> spots = new List<Vector3>();
        private int terrainLayer;
        private int solidMask;
        private int obstacleMask;

        private void OnServerInitialized()
        {
            terrainLayer = LayerMask.NameToLayer("Terrain");
            solidMask = LayerMask.GetMask("Terrain", "World", "Construction", "Deployed", "Default", "Tree");
            obstacleMask = LayerMask.GetMask("World", "Construction", "Deployed", "Default", "Tree");
            FindSpots(5);
            Puts(spots.Count > 0
                ? $"{spots.Count} spawn spot(s), first {spots[0]} (terrain layer {terrainLayer})"
                : "no safe spawn spot found - vanilla spawning stays active");
        }

        private object OnFindSpawnPoint(BasePlayer player, ulong teamId)
        {
            if (spots.Count == 0)
            {
                return null;
            }
            Vector3 spot = spots[Random.Range(0, spots.Count)];
            return new BasePlayer.SpawnPoint { pos = spot, rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) };
        }

        [ConsoleCommand("devspawn.test")]
        private void CmdTest(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null)
            {
                return;
            }
            foreach (Vector3 spot in spots)
            {
                Puts($"spot {spot}: terrain {TerrainMeta.HeightMap.GetHeight(spot):0.00}, slope {TerrainMeta.HeightMap.GetSlope(spot):0.0}, insideTerrain={AntiHack.TestInsideTerrain(spot)}");
            }
            if (spots.Count == 0)
            {
                Puts("no spots");
            }
            BasePlayer.SpawnPoint game = ServerMgr.FindSpawnPoint(null, 0uL);
            Puts($"the game's spawn search now returns {game.pos}, insideTerrain={AntiHack.TestInsideTerrain(game.pos)}");
        }

        // Spiral out from the map centre; keep the first few spots that are safe to stand on.
        private void FindSpots(int wanted)
        {
            float half = TerrainMeta.Size.x * 0.5f - 50f;
            for (float radius = 0f; radius < half && spots.Count < wanted; radius += Step)
            {
                int samples = radius < 1f ? 1 : Mathf.CeilToInt(2f * Mathf.PI * radius / Step);
                for (int i = 0; i < samples && spots.Count < wanted; i++)
                {
                    float angle = i * 2f * Mathf.PI / samples;
                    Vector3 spot;
                    if (IsSafe(new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius), out spot) && !NearOther(spot))
                    {
                        spots.Add(spot);
                    }
                }
            }
        }

        private bool IsSafe(Vector3 probe, out Vector3 spot)
        {
            spot = probe;
            RaycastHit hit;
            if (!Physics.Raycast(new Vector3(probe.x, 1000f, probe.z), Vector3.down, out hit, 2000f, solidMask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }
            if (hit.collider.gameObject.layer != terrainLayer)
            {
                return false; // a rock, tree or building stands here
            }
            spot = hit.point + Vector3.up * 0.1f;
            if (spot.y < WaterSystem.OceanLevel + 3f)
            {
                return false; // beach or sea
            }
            if (TerrainMeta.WaterMap != null && TerrainMeta.WaterMap.GetHeight(spot) > hit.point.y - 0.5f)
            {
                return false; // river or lake
            }
            if (TerrainMeta.HeightMap.GetSlope(spot) > MaxSlope)
            {
                return false;
            }
            if (TerrainMeta.Collision != null && TerrainMeta.Collision.GetIgnore(spot))
            {
                return false; // cave or tunnel mouth
            }
            if (Physics.CheckCapsule(spot + Vector3.up * 0.6f, spot + Vector3.up * 1.9f, 0.6f, obstacleMask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }
            if (TerrainMeta.Path != null)
            {
                foreach (MonumentInfo monument in TerrainMeta.Path.Monuments)
                {
                    if (monument != null && monument.Distance(spot) < MonumentGap)
                    {
                        return false;
                    }
                }
            }
            return !AntiHack.TestInsideTerrain(spot);
        }

        private bool NearOther(Vector3 spot)
        {
            foreach (Vector3 other in spots)
            {
                if ((other - spot).sqrMagnitude < 15f * 15f)
                {
                    return true;
                }
            }
            return false;
        }
    }
}

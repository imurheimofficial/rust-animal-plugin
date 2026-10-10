using UnityEngine;
using UnityEngine.AI;

namespace Oxide.Plugins
{
    [Info("ProbeAI", "Beastmaster dev", "0.0.1")]
    [Description("DEV ONLY: does the server give plugins UnityEngine.AIModule without a // Reference?")]
    public class ProbeAI : RustPlugin
    {
        private void OnServerInitialized()
        {
            NavMeshHit hit;
            bool any = NavMesh.SamplePosition(Vector3.zero, out hit, 50f, NavMesh.AllAreas);
            Puts("PROBE ai ok, unity navmesh sample=" + any);
        }
    }
}

using UnityEngine;

namespace Oxide.Plugins
{
    [Info("ProbePhysics", "Beastmaster dev", "0.0.1")]
    [Description("DEV ONLY: does the server give plugins UnityEngine.PhysicsModule without a // Reference?")]
    public class ProbePhysics : RustPlugin
    {
        private void OnServerInitialized()
        {
            RaycastHit hit;
            bool any = Physics.Raycast(new Vector3(0f, 500f, 0f), Vector3.down, out hit, 1000f);
            Puts("PROBE physics ok, raycast=" + any + (any ? " y=" + hit.point.y : string.Empty));
        }
    }
}

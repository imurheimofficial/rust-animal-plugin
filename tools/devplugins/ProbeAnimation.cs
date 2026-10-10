using UnityEngine;

namespace Oxide.Plugins
{
    [Info("ProbeAnimation", "Beastmaster dev", "0.0.1")]
    [Description("DEV ONLY: does the server give plugins UnityEngine.AnimationModule without a // Reference?")]
    public class ProbeAnimation : RustPlugin
    {
        private void OnServerInitialized()
        {
            AnimationClip clip = null;
            Puts("PROBE animation ok, clip type=" + typeof(AnimationClip).FullName + " null=" + (clip == null));
        }
    }
}

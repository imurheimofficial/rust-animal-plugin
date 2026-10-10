using Oxide.Core;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("HarnessSmoke", "Beastmaster dev", "0.0.1")]
    [Description("Dev only: proves that the compile harness and the dev server agree.")]
    public class HarnessSmoke : RustPlugin
    {
        private void OnServerInitialized()
        {
            Puts($"HarnessSmoke loaded on Rust {Rust.Protocol.network} / Unity {Application.unityVersion}");
        }

        [ConsoleCommand("harness.ping")]
        private void CmdPing(ConsoleSystem.Arg arg)
        {
            arg.ReplyWith("harness.pong " + OxideMod.Version);
        }
    }
}

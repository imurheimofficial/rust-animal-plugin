using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("ProbeJson", "Beastmaster dev", "0.0.1")]
    [Description("DEV ONLY: is Newtonsoft.Json available to plugins?")]
    public class ProbeJson : RustPlugin
    {
        private class Sample
        {
            [JsonProperty("Value")]
            public int Value = 7;
        }

        private void OnServerInitialized()
        {
            Puts("PROBE json ok: " + JsonConvert.SerializeObject(new Sample()));
        }
    }
}

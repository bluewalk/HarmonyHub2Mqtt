using System;
using System.Dynamic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Harmony;
using Net.Bluewalk.LogTools;
using Newtonsoft.Json;

namespace Net.Bluewalk.HarmonyHub2Mqtt
{
    public static class MqttHubHelper
    {
        public static async Task MqttSetActivity(this Hub hub, string message)
        {
            if (!string.IsNullOrEmpty(message))

                if (int.TryParse(message, out _))
                    await hub.StartActivity(hub.GetActivityById(message));
                else
                    await hub.StartActivity(hub.GetActivityByLabel(message));
            else
                await hub.EndActivity();
        }

        public static async Task MqttChangeChannel(this Hub hub, string message)
        {
            if (!int.TryParse(message, out _))
                throw new MqttHubHelperException($"{message} is not a channel-number");

            await hub.ChangeChannel(message);
        }

        public static async Task MqttButton(this Hub hub, string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            try
            {
                if (hub.GetRunningActivity() == null) throw new MqttHubHelperException("No running activity");

                var function = hub.GetRunningActivity()?.GetFunctionByName(message);
                if (function == null) throw new MqttHubHelperException($"Function '{message}' does not exist");

                await hub.PressButtonAsync(function);
            }
            catch (Exception ex)
            {
                Logger.LogMessage("Error upon sending button-press");
                Logger.LogException(ex);
            }
        }

        public static async Task MqttButtons(this Hub hub, string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            try
            {
                if (hub.GetRunningActivity() == null) throw new MqttHubHelperException("No running activity");

                var buttons = JsonConvert.DeserializeObject<PressButtons>(message);
                var functions = buttons.Functions
                    .Select(b =>
                    {
                        var function = hub.GetRunningActivity()?.GetFunctionByName(b);
                        if (function == null) throw new MqttHubHelperException($"Function '{message}' does not exist");
                        return function;
                    })
                    .ToList();

                await hub.PressButtonsAsync(buttons.Delay, functions);
            }
            catch (Exception ex)
            {
                Logger.LogMessage("Error upon sending button-presses");
                Logger.LogException(ex);
            }
        }

        public static async Task MqttSync(this Hub hub)
        {
            await hub.SyncConfigurationAsync();
            await hub.UpdateStateAsync();
        }

        public static async Task<HubInfo> DiscoverByIp(IPAddress ip)
        {
            var Client = new WebClient();
            Client.Headers.Add("Origin", "http://localhost.nebula.myharmony.com");
            Client.Headers.Add(HttpRequestHeader.ContentType, "application/json");
            Client.Headers.Add(HttpRequestHeader.Accept, "application/json");
            Client.Headers.Add(HttpRequestHeader.AcceptCharset, "utf-8");

            return JsonConvert
                .DeserializeObject<IpDiscoveryResult>(await Client.DownloadStringTaskAsync($"http://{ip}:8088/"))
                ?.HubInfo;
        }
    }

    public class MqttHubHelperException : Exception
    {
        public MqttHubHelperException(string message) : base(message)
        {

        }
    }

    public class IpDiscoveryResult
    {
        [JsonProperty("msg")]
        public string Message { get; set; }
        [JsonProperty("data")]
        public HubInfo HubInfo { get; set; }
        [JsonProperty("code")]
        public string Code { get; set; }
    }
}
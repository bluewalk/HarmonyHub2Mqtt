using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using Harmony;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using Newtonsoft.Json;

namespace Net.Bluewalk.HarmonyHub2Mqtt
{
    public class Hub2MqttLogic
    {
        private readonly DiscoveryService _discoveryService;
        private readonly IManagedMqttClient _mqttClient;
        private readonly string _mqttHost;
        private readonly int _mqttPort;
        private readonly string _mqttRootTopic;

        private List<Hub> _hubs;
        private readonly DeviceID _deviceId = new DeviceID(Guid.NewGuid().ToString("N"), "BluewalkHarmony2Mqtt", Environment.MachineName);
        private static string _hubFileName;

        public Hub2MqttLogic(string mqttHost, int mqttPort, string mqttRootTopic, string hubFileName = null)
        {
            _mqttRootTopic = mqttRootTopic;
            _mqttHost = mqttHost;
            _mqttPort = mqttPort;

            _hubFileName = hubFileName ?? "hubs.json";

            _mqttClient = new MqttFactory().CreateManagedMqttClient();
            _mqttClient.ApplicationMessageReceived += MqttClientOnApplicationMessageReceived;

            _hubs = new List<Hub>();

            LoadHubListFromFile();

            _discoveryService = new DiscoveryService();
            _discoveryService.HubFound += async (sender, e) =>
            {
                var hub = new Hub(e.HubInfo);

                if (_hubs.Any(h => h.Info.RemoteId.Equals(hub.Info.RemoteId))) return;

                await PrepareHub(hub);
                _hubs.Add(hub);
            };
        }

        public void LoadHubListFromFile(string fileName = null)
        {
            if (!File.Exists(fileName ?? _hubFileName))
                throw new FileNotFoundException();

            var json = File.ReadAllText(fileName);

            _hubs = JsonConvert.DeserializeObject<List<Hub>>(json);
            _hubs?.ForEach(async h => await PrepareHub(h));
        }

        public void SaveHubListToFile(string fileName = null)
        {
            File.WriteAllText(fileName ?? _hubFileName, JsonConvert.SerializeObject(_hubs));
        }

        private async Task PrepareHub(Hub hub)
        {
            hub.OnActivityProgress += async (o, args) =>
                await Publish($"{hub.Info.RemoteId}/activity/{args.Activity.Id}/progress", args.Progress);
            hub.OnActivityRan += async (o, args) =>
                await Publish($"{hub.Info.RemoteId}/activity/current", args.Response.Data);
            hub.OnChannelChanged += async (o, args) =>
                await Publish($"{hub.Info.RemoteId}/channel/current", args.Response.Data);
            hub.OnHubSynchronized += async (o, args) =>
                await Publish($"{hub.Info.RemoteId}/syncstatus", args.Response.Data);
            hub.OnStateDigestReceived += async (o, args) =>
                await Publish($"{hub.Info.RemoteId}/state", args.Response.Data);

            SubscribeTopic($"{hub.Info.RemoteId}/activity");
            SubscribeTopic($"{hub.Info.RemoteId}/channel");

            await hub.ConnectAsync(_deviceId);
            await hub.SyncConfigurationAsync();
            await hub.UpdateStateAsync();
        }

        #region MQTT stuff

        private async Task Publish(string topic, object message = null, bool retain = true)
        {
            await Publish(topic, JsonConvert.SerializeObject(message), retain);
        }

        private async Task Publish(string topic, string message, bool retain = true)
        {
            if (_mqttClient == null || !_mqttClient.IsConnected) return;
            topic = $"{_mqttRootTopic}/{topic}";
#if DEBUG
            topic = $"dev/{topic}";
#endif
            var msg = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(message)
                .WithExactlyOnceQoS()
                .WithRetainFlag()
                .Build();

            await _mqttClient.PublishAsync(msg);
        }

        private async void SubscribeTopic(string topic)
        {
            if (_mqttClient == null || !_mqttClient.IsConnected) return;
            topic = $"{_mqttRootTopic}/{topic}";

#if DEBUG
            topic = $"dev/{topic}";
#endif
            await _mqttClient.SubscribeAsync(new TopicFilterBuilder().WithTopic(topic).Build());
        }

        private async void MqttClientOnApplicationMessageReceived(object sender, MqttApplicationMessageReceivedEventArgs e)
        {
            var topic = e.ApplicationMessage.Topic.ToUpper().Split('/');
            var message = e.ApplicationMessage.ConvertPayloadToString();
#if DEBUG
            // Remove first part "dev"
            topic = topic.Skip(1).ToArray();
#endif
            /**
             * Topic[0] = _rootTopic
             * Topic[1] = RemoteId
             * Topic[2] = Activity | Channel
             */
            var hub = _hubs.FirstOrDefault(h => h.Info.RemoteId.Equals(topic[1]));
            if (hub == null) return;

            switch (topic[2])
            {
                case "ACTIVITY":
                    if (!string.IsNullOrEmpty(message))
                        await hub.StartActivity(new Activity() { Id = message });
                    else
                        await hub.EndActivity();
                    break;
                case "CHANNEL":
                    await hub.ChangeChannel(message);
                    break;
            }
        }
        #endregion

        public async Task Start()
        {
            var options = new ManagedMqttClientOptionsBuilder()
                .WithAutoReconnectDelay(TimeSpan.FromSeconds(5))
                .WithClientOptions(new MqttClientOptionsBuilder()
                    .WithClientId($"BluewalkHarmony2Mqtt-{Environment.MachineName}")
                    .WithTcpServer(_mqttHost, _mqttPort)
                    .WithTls().Build())
                .Build();

            await _mqttClient.StartAsync(options);

            _discoveryService.StartDiscovery();
        }

        public async Task Stop()
        {
            _discoveryService.StopDiscovery();
            _hubs.ForEach(async h => await h.Disconnect());
            _mqttClient?.StopAsync();

            SaveHubListToFile();
        }
    }
}

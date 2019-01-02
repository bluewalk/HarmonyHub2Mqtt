using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Harmony;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using Net.Bluewalk.LogTools;
using Newtonsoft.Json;

namespace Net.Bluewalk.HarmonyHub2Mqtt
{
    public class Hub2MqttLogic
    {
        private readonly IManagedMqttClient _mqttClient;
        private readonly string _mqttHost;
        private readonly int _mqttPort;
        private readonly string _mqttRootTopic;

        private List<Hub> _hubs;
        private readonly DeviceID _deviceId = new DeviceID(Guid.NewGuid().ToString("N"), "BluewalkHarmony2Mqtt", Environment.MachineName);
        private static string _hubFileName;

        public Hub2MqttLogic(string mqttHost, int mqttPort, string mqttRootTopic, string hubFileName = null)
        {
            Logger.Initialize();

            _mqttRootTopic = mqttRootTopic;
            _mqttHost = mqttHost;
            _mqttPort = mqttPort;

            _hubFileName = hubFileName ?? Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "hubs.json");

            _mqttClient = new MqttFactory().CreateManagedMqttClient();
            _mqttClient.ApplicationMessageReceived += MqttClientOnApplicationMessageReceived;
            _mqttClient.Connected += (sender, args) => Logger.LogMessage("MQTT: Connected");
            _mqttClient.ConnectingFailed += (sender, args) =>
                Logger.LogMessage("MQTT: Unable to connect ({0})", args.Exception.Message);
            _mqttClient.Disconnected += (sender, args) => Logger.LogMessage("MQTT: Disconnected");

            _hubs = new List<Hub>();

            LoadHubListFromFile();
        }

        public void LoadHubListFromFile()
        {
            if (!File.Exists(_hubFileName))
            {
                Logger.LogMessage("File {0} does not exist", _hubFileName);
                return;
            }

            Logger.LogMessage("Loading existing hubs from {0}", _hubFileName);
            var json = File.ReadAllText(_hubFileName);

            _hubs = JsonConvert.DeserializeObject<List<Hub>>(json);
            Logger.LogMessage("{0} hub(s) loaded", _hubs.Count);
            _hubs?.ForEach(async h => await PrepareHub(h));
        }

        public void SaveHubListToFile()
        {
            Logger.LogMessage("Saving hubs to {0}", _hubFileName);
            File.WriteAllText(_hubFileName, JsonConvert.SerializeObject(_hubs));
        }

        private async Task PrepareHub(Hub hub)
        {
            hub.OnActivityProgress += async (o, args) =>
                await Publish($"{hub.Info.RemoteId}/activity/{args?.Activity.Id}/progress", args.Progress);
            hub.OnActivityRan += async (o, args) =>
                await Publish($"{hub.Info.RemoteId}/activity/current", args.Response.Data);
            hub.OnChannelChanged += async (o, args) =>
                await Publish($"{hub.Info.RemoteId}/channel/current", args.Response.Data);
            hub.OnHubSynchronized += async (o, args) =>
                await Publish($"{hub.Info.RemoteId}/sync-status", args.Response.Data);
            hub.OnStateDigestReceived += async (o, args) =>
                await Publish($"{hub.Info.RemoteId}/state", args.Response.Data);

            SubscribeTopic($"{hub.Info.RemoteId}/activity");
            SubscribeTopic($"{hub.Info.RemoteId}/channel");

            Logger.LogMessage("Hub: Connecting to {0} at {1}", hub.Info.FriendlyName, hub.Info.IP);
            await hub.ConnectAsync(_deviceId);
            Logger.LogMessage("Hub: Synchronizing configuration for {0}", hub.Info.FriendlyName);
            await hub.SyncConfigurationAsync();
            Logger.LogMessage("Hub: Updating state for {0}", hub.Info.FriendlyName);
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
            Logger.LogMessage("MQTT: Publishing message to {0}", topic);

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
            topic = $"{_mqttRootTopic}/{topic}";

#if DEBUG
            topic = $"dev/{topic}";
#endif
            Logger.LogMessage("MQTT: Subscribing to {0}", topic);

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
             * Topic[2] = Activity | Channel | Sync
             */
            var hub = _hubs.FirstOrDefault(h => h.Info.RemoteId.Equals(topic[1]));
            if (hub == null) return;

            try
            {
                switch (topic[2])
                {
                    case "ACTIVITY":
                        if (!string.IsNullOrEmpty(message))
                            await hub.StartActivity(new Activity() {Id = message});
                        else
                            await hub.EndActivity();
                        break;
                    case "CHANNEL":
                        await hub.ChangeChannel(message);
                        break;
                    case "SYNC":
                        await hub.SyncConfigurationAsync();
                        await hub.UpdateStateAsync();
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
            }
        }
        #endregion

        public async Task PerformDiscovery()
        {
            Logger.LogMessage("Discovery: Starting");
            var discoveryService = new DiscoveryService();
            discoveryService.HubFound += async (sender, e) =>
            {
                if (_hubs.Any(h => h.Info.RemoteId.Equals(e.HubInfo.RemoteId))) return;

                var hub = new Hub(e.HubInfo);
                Logger.LogMessage("Discovery: Found hub {0} at {1}", hub.Info.FriendlyName, hub.Info.IP);

                await PrepareHub(hub);
                _hubs.Add(hub);
            };


            var waitCancellationToken = new CancellationTokenSource();
            discoveryService.StartDiscovery();

            try
            {
                await Task.Delay(30000, waitCancellationToken.Token);
            }
            catch (OperationCanceledException)
            {

            }
            Logger.LogMessage("Discovery: Stopping");
            discoveryService.StopDiscovery();
        }

        public async Task Start()
        {
            var options = new ManagedMqttClientOptionsBuilder()
                .WithAutoReconnectDelay(TimeSpan.FromSeconds(5))
                .WithClientOptions(new MqttClientOptionsBuilder()
                    .WithClientId($"BluewalkHarmony2Mqtt-{Environment.MachineName}")
                    .WithTcpServer(_mqttHost, _mqttPort))
                .Build();

            Logger.LogMessage("MQTT: Connecting to {0}:{1}", _mqttHost, _mqttPort);
            await _mqttClient.StartAsync(options);

            await Task.Run(PerformDiscovery);
        }

        public async Task Stop()
        {
            _hubs.ForEach(async h => await h.Disconnect());
            await _mqttClient?.StopAsync();

            SaveHubListToFile();
        }
    }
}

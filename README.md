# HarmonyHub2Mqtt
HarmonyHub2Mqtt is a Windows service which connects to your Logitech Harmony Hubs and relays statusses via MQTT.

## Installation
Extract the release build to a folder and run `Net.Bluewalk.HarmonyHub2Mqtt.Service.exe --install`
This will install the service

## Configuration
Edit the `Net.Bluewalk.HarmonyHub2Mqtt.Service.exe.config` file and set the following settings accordingly under 
```
<configuration>
  <appSettings>
    <add key="MQTT_Host" value="127.0.0.1" />
    <add key="MQTT_Port" value="1883" />
    <add key="MQTT_RootTopic" value="hue" />
  </appSettings>
  ```

| Configuration setting | Description |
|-|-|
| MQTT_Host | IP address / DNS of the MQTT broker |
| MQTT_Port | Port of the MQTT broker |
| MQTT_RootTopic | This text will be prepended to the MQTT Topic `/[remoteId]/#` |

## Starting/stopping
Go to services.msc to start/stop the `Bluewalk HarmonyHub2Mqtt` service or run `net start BluewalkHarmonyHub2Mqtt` or `net stop BluewalkHarmonyHub2Mqtt`

Once started the service will start discovering Hubs in the network and will automatically connect to them. When the service is stopped the discovered hubs will be saved in `hubs.json` (same folder as the service).
If you run this service on another - routed - network than the hubs are on, broadcasts will most likely not get through to the hubs and no hubs will be found. In that case you can create `hubs.json` yourself containing the required information.

## hubs.json layout
The layout consists of an array of objects lik `{"Info": {"ip": "192.168.1.10","remoteId": "13265782"}}`, example:
```
[
    {
        "Info": {
            "ip": "192.168.1.X",
            "remoteId": "12345678"
        }
    }
]
```
You are required to know the remoteId of the hub in order to connect. If you don't know this Id you will have to run the service on the same network __once__ or if you know the IP-adrress, send a `POST` request with the following headers and body to `http://{ip}:8088/`:
#### headers
```
Content-Type: application/json
Origin: http://localhost.nebula.myharmony.com
Accept: application/json
Accept-Charset: utf-8
```
#### body
```
{
"id ": 124,
"cmd": "connect.discoveryinfo?get",
"params": {}
}
```
The hub will - after some time - return a JSON object containing the `remoteId`.

## MQTT Topics
| Topic | Type | Data |
|-|-|-|
| {remoteId}/activity/{id}/procress | readonly | The progress of the current activity activation|
| {remoteId}/activity/current | readonly | JSON object containing details of the active activity (updated on activity change)|
| {remoteId}/chanel/current | readonly | JSON object containing details of the current channel (updated on channel change)|
| {remoteId}/state | readonly | JSON object containing details of the state (updated on state change)|
| {remoteId}/activity | write | Send `id` of an activity to this channel to start the activity associated with the `id` or leave empty to stop active activity|
| {remoteId}/channel | write | Send `id` of an channel to this channel to start the channel associated with the `id`|

## Uninstall
1. Stop the service
2. Run `Net.Bluewalk.HarmonyHub2Mqtt.Service.exe --uninstall`
3. Delete files
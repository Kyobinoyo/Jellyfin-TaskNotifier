# Home Assistant Task Notifier (Jellyfin plugin)

Toggles a Home Assistant sensor to `on` while a Jellyfin scheduled task (e.g. "Scan
Media Library") is running, and back to `off` once none are running anymore — so you
can trigger Home Assistant automations off the sensor's state changes. Optionally also
fires a discrete Home Assistant event per task start/finish for per-task automations.

Built for **Jellyfin 12.0** (ABI `12.0.0.0`, .NET 10).

## How it works

- `TaskNotifierService` subscribes to `ITaskManager.TaskExecuting` / `TaskCompleted`.
- When the first tracked task starts, it `POST`s to
  `{HomeAssistantUrl}/api/states/{SensorEntityId}` with `{"state": "on", ...}`.
- When the last tracked task finishes, it posts `{"state": "off", ...}` to the same
  entity.
- If enabled, it also `POST`s to `{HomeAssistantUrl}/api/events/{EventType}` with the
  task key/name/phase/status on every start and finish.
- "Tracked" tasks are controlled by the `IncludedTaskKeys` setting — leave empty to
  track every scheduled task, or list specific task keys (see Jellyfin's Dashboard →
  Scheduled Tasks) to only watch e.g. the library scan.

## Build

Requires the .NET 10 SDK.

```bash
dotnet build -c Release
```

Before building, open `Jellyfin.Plugin.HomeAssistantTaskNotifier.csproj` and make sure
the `Jellyfin.Controller` / `Jellyfin.Model` NuGet package version and `meta.json`'s
`targetAbi` match your **exact** installed Jellyfin server version (Dashboard →
General). A mismatch makes Jellyfin refuse to load the plugin ("Not supported").

## Install

1. `dotnet build -c Release`
2. Create a folder named `Home Assistant Task Notifier_1.0.0.0` inside your Jellyfin
   data directory's `plugins` folder (e.g. `/var/lib/jellyfin/plugins/` on Linux,
   `%ProgramData%\Jellyfin\Server\plugins\` on Windows).
3. Copy into that folder:
   - `bin/Release/net10.0/Jellyfin.Plugin.HomeAssistantTaskNotifier.dll`
   - `meta.json`
4. Restart Jellyfin.
5. Dashboard → Plugins → "Home Assistant Task Notifier" → configure:
   - **Home Assistant URL**: e.g. `http://homeassistant.local:8123`
   - **Long-Lived Access Token**: Home Assistant → your profile → Security →
     Long-Lived Access Tokens → Create Token
   - **Sensor entity id**: default `binary_sensor.jellyfin_task_running`
   - **Task keys to track**: leave empty for all tasks, or e.g. `RefreshLibrary` to
     only track the library scan
   - Optionally enable per-task events

## Using it in Home Assistant

The sensor is pushed via the REST API, so no YAML/integration is required on the HA
side to receive it — it appears as soon as the plugin first posts to it. Note that a
state pushed this way is **not** restored across a Home Assistant restart (it will
show `unavailable` until Jellyfin next runs a tracked task) since there's no backing
integration/device — that's expected for this style of push sensor.

Example automation trigger:

```yaml
trigger:
  - platform: state
    entity_id: binary_sensor.jellyfin_task_running
    to: "on"
```

If you enabled per-task events, you can instead trigger on the event, e.g.:

```yaml
trigger:
  - platform: event
    event_type: jellyfin_scheduled_task
    event_data:
      task_key: RefreshLibrary
      phase: started
```

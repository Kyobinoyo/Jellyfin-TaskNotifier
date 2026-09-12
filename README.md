# Jellyfin Task Notifier

A [Jellyfin](https://jellyfin.org/) plugin that bridges scheduled tasks to
[Home Assistant](https://www.home-assistant.io/): a sensor turns `on` while a tracked
task (e.g. "Scan Media Library") is running and `off` again once nothing tracked is
running anymore, so you can trigger automations off its state changes. Optionally it
also fires a discrete Home Assistant event on every task start/finish for per-task
automations.

Compatible with **Jellyfin 12.0** (ABI `12.0.0.0`, .NET 10).

## Features

- Pushes a running/idle sensor state to Home Assistant via its REST API — no
  Home Assistant integration or YAML setup required.
- Optionally fires a Home Assistant event per task start/finish, carrying the task
  key, name, phase, and result status.
- Choose which scheduled tasks to track (all of them, or a specific allow-list).
- Installable and updatable directly from Jellyfin's plugin catalog.

## Requirements

- Jellyfin server 12.0.x.
- A Home Assistant instance reachable from the Jellyfin server, and a
  [long-lived access token](https://www.home-assistant.io/docs/authentication/#your-long-lived-access-token).

## Installation

### Via the plugin repository (recommended)

1. In Jellyfin, go to **Dashboard → Plugins → Repositories → Add Repository**.
2. Give it any name, and set the repository URL to:
   ```
   https://raw.githubusercontent.com/Kyobinoyo/Jellyfin-TaskNotifier/main/manifest.json
   ```
3. Save, then open the **Catalog** tab — "Home Assistant Task Notifier" appears
   under General. Install it and restart Jellyfin.
4. Configure it under **Dashboard → Plugins → Home Assistant Task Notifier**
   (see [Configuration](#configuration) below).

Future updates are picked up the same way any other catalog plugin update is.

### Manual install

1. Download the latest release zip from the
   [Releases page](https://github.com/Kyobinoyo/Jellyfin-TaskNotifier/releases).
2. Create a folder named `Home Assistant Task Notifier_<version>` inside your
   Jellyfin data directory's `plugins` folder (e.g. `/var/lib/jellyfin/plugins/` on
   Linux, `%ProgramData%\Jellyfin\Server\plugins\` on Windows).
3. Extract the zip's `.dll` into that folder, alongside a `meta.json` (see the one in
   this repo's root as a template — update `version` to match).
4. Restart Jellyfin and configure the plugin as above.

## Configuration

| Setting | Description | Default |
|---|---|---|
| Home Assistant URL | Base URL of your Home Assistant instance, e.g. `http://homeassistant.local:8123` | _(empty)_ |
| Long-Lived Access Token | Token from Home Assistant → your profile → Security → Long-Lived Access Tokens | _(empty)_ |
| Sensor entity id | Entity that is toggled `on`/`off` while a tracked task is running | `binary_sensor.jellyfin_task_running` |
| Task keys to track | Comma-separated scheduled task keys; empty tracks every task | _(empty, all tasks)_ |
| Also fire a Home Assistant event | Fires an event per task start/finish in addition to the sensor | enabled |
| Event type name | Home Assistant event type used when the above is enabled | `jellyfin_scheduled_task` |
| Allow self-signed / invalid HTTPS certificate | Accept an invalid certificate when calling Home Assistant | disabled |

Task keys are shown next to each task under **Dashboard → Scheduled Tasks** in
Jellyfin; the built-in library scan uses the key `RefreshLibrary`.

## How it works

`TaskNotifierService` subscribes to `ITaskManager.TaskExecuting` / `TaskCompleted`:

- When the first tracked task starts, it `POST`s
  `{"state": "on", ...}` to `{HomeAssistantUrl}/api/states/{SensorEntityId}`.
- When the last tracked task finishes, it posts `{"state": "off", ...}` to the same
  entity.
- If enabled, it also `POST`s to `{HomeAssistantUrl}/api/events/{EventType}` with the
  task key/name/phase/status on every start and finish.

## Using it in Home Assistant

The sensor is pushed via the REST API, so it appears automatically as soon as the
plugin first posts to it — no entity needs to be predefined in Home Assistant. Note
that a state pushed this way is **not** restored across a Home Assistant restart (it
shows `unavailable` until Jellyfin next runs a tracked task), since there's no backing
integration or device — that's expected for this style of push sensor.

Example automation trigger on the sensor:

```yaml
trigger:
  - platform: state
    entity_id: binary_sensor.jellyfin_task_running
    to: "on"
```

If per-task events are enabled, you can trigger on a specific task instead:

```yaml
trigger:
  - platform: event
    event_type: jellyfin_scheduled_task
    event_data:
      task_key: RefreshLibrary
      phase: started
```

## Building from source

Requires the .NET 10 SDK.

```bash
dotnet build -c Release
```

The `Jellyfin.Controller` / `Jellyfin.Model` NuGet package versions referenced in the
`.csproj`, and `meta.json`'s `targetAbi`, must match the Jellyfin server version you're
building against — a mismatch makes Jellyfin refuse to load the plugin
("Not supported"). The built DLL is at
`bin/Release/net10.0/Jellyfin.Plugin.HomeAssistantTaskNotifier.dll`.

## Releasing (maintainers)

Pushing a four-part version tag (matching the C# assembly version format
`major.minor.build.revision`) triggers `.github/workflows/release.yml`, which builds
the plugin, publishes a GitHub release with the packaged zip, and updates
`manifest.json` so the new version shows up for anyone using the plugin repository:

```bash
git tag v1.0.0.0
git push origin v1.0.0.0
```

The workflow targets ABI `12.0.0.0` (Jellyfin 12.0) by default — update that in
`release.yml` (and the matching NuGet package versions in the `.csproj`) when building
for a different Jellyfin server version.

## Contributing

Issues and pull requests are welcome — please include your Jellyfin server version
and, for bugs, the relevant Jellyfin server log output.

<!-- Repository & Project Info -->
[![GitHub release (latest by date)](https://img.shields.io/github/v/release/Kyobinoyo/Jellyfin-TaskNotifier?style=flat-square&logo=github&color=blue)](https://github.com/Kyobinoyo/Jellyfin-TaskNotifier/releases)
[![GitHub License](https://img.shields.io/badge/License-GPL--3.0-green?style=flat-square&logo=gnu)](LICENSE)
[![GitHub issues](https://img.shields.io/github/issues/Kyobinoyo/Jellyfin-TaskNotifier?style=flat-square)](https://github.com/Kyobinoyo/Jellyfin-TaskNotifier/issues)

<!-- Ecosystem & Tech Stack -->
[![Jellyfin](https://img.shields.io/badge/Jellyfin-Plugin-00A4DC?style=flat-square&logo=jellyfin&logoColor=white)](https://jellyfin.org/)
[![C# / .NET](https://img.shields.io/badge/C%23-.NET-512BD4?style=flat-square&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)

<!-- Stats & Community -->
[![GitHub stars](https://img.shields.io/github/stars/Kyobinoyo/Jellyfin-TaskNotifier?style=flat-square)](https://github.com/Kyobinoyo/Jellyfin-TaskNotifier/stargazers)
[![GitHub downloads (all releases)](https://img.shields.io/github/downloads/Kyobinoyo/Jellyfin-TaskNotifier/total?style=flat-square&color=orange)](https://github.com/Kyobinoyo/Jellyfin-TaskNotifier/releases)

# Jellyfin Task Notifier

### **Made with Claude (AI) (just to be transparent)**

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
| Sensor entity id | Entity that is toggled `on`/`off` while a tracked task is running — see [Creating the sensor](#creating-the-sensor-in-home-assistant) | `binary_sensor.jellyfin_task_running` |
| Task keys to track | Comma-separated scheduled task keys; empty tracks every task | _(empty, all tasks)_ |
| Periodic status refresh interval | Minutes between periodic re-sends of the current running/idle state; `0` disables it | `5` |
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
- Every `StatusRefreshIntervalMinutes` (default 5) it reads which tracked tasks are
  actually running and re-sends the sensor state. This corrects missed updates (e.g.
  Home Assistant briefly unreachable) and brings a `binary_sensor.*` entity back after
  a Home Assistant restart.

## Creating the sensor in Home Assistant

There are two ways to set up the entity behind `Sensor entity id`, depending on
whether you want zero setup or a persistent, manageable entity.

### Option A: Do nothing (quick, entity id in the `binary_sensor.*`/`sensor.*` domain)

Leave the default `binary_sensor.jellyfin_task_running`, or pick any id outside the
`input_boolean` domain. The plugin pushes its state straight into the state machine
via the REST API (`POST /api/states/...`), so the entity appears by itself the first
time a tracked task runs — nothing to create in Home Assistant beforehand.

Trade-off: since there's no integration or device behind it, the entity does **not**
survive a Home Assistant restart — it simply won't exist again until Jellyfin's next
task run posts to it. It also won't show up under Settings → Devices & Services →
Entities (no registry entry), only in Developer Tools → States and in the entity
pickers of the dashboard/automation editors.

### Option B: Create an `input_boolean` helper (recommended for a persistent entity)

1. Home Assistant → **Settings → Devices & Services → Helpers**.
2. **+ Add Helper → Toggle**.
3. Name it, e.g. `Jellyfin Task Running` (Home Assistant derives an entity id such as
   `input_boolean.jellyfin_task_running`).
4. Save, then set that entity id (with the `input_boolean.` prefix) as the plugin's
   `Sensor entity id`.

The plugin detects the `input_boolean.` prefix and calls its `turn_on`/`turn_off`
service instead of overwriting the raw state, which is the correct way to drive a
helper. It shows up under Settings → Entities like any other helper (rename, icon,
area assignment) and survives Home Assistant restarts. The only limitation: helpers
don't carry the `running_tasks` attribute that Option A's sensor gets, since a service
call can only flip on/off, not attach custom attributes.

Example automation trigger on the sensor (works the same for either option, just
adjust the entity id):

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

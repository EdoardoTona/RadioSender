# RadioSender

RadioSender decodes and routes live timing data between timing devices, race-management software, results services and custom receivers. It runs as a desktop application with Photino, or in a local browser with the desktop window disabled.

Configure the data flow visually: connect sources to one or more targets, add named filters and mappings to connections, and use processors for deduplication or shared Oribos enrichment. Each node has its own stream inspector, replay controls and logs. Sources and targets can be added, removed or reconfigured while the application runs.

## Start a flow

1. Start RadioSender and choose **New** to create a JSON configuration, or **Open** to load one. New asks where to save before opening the editor.
2. In **Editor**, add modules, configure their settings and connect their ports. Select a connection to add a reusable filter or a delay.
3. Changes autosave. Choose **Apply** and confirm the affected nodes to activate the saved configuration.
4. In **Flow**, select a node to inspect events, review deliveries, replay data or use its controls. **Manual input** provides fields for entering events; **TmF Radio** provides its own radio-network view and ping command.
5. Use **Edit** to change the running flow, **Save As** to create a copy, or **Stop** to stop its modules.

Saving a document and applying it are separate operations. Opening another file leaves the current flow running until Stop or Apply. On application startup, no flow runs automatically.

See the [Flow guide](docs/flow-editor.md) for runtime behavior, replay, file conflicts and module development. Try the [example configurations](examples), starting with [Manual input and two output branches](examples/manual-branches.radiosender.json). Copy an example into a writable folder before opening it; relative output paths use that folder.

## Supported modules

| Role | Modules |
| --- | --- |
| Sources | Manual input, formatted TCP client/server, Microgate REI2 TCP/serial, Microplus UDP, OBR UDP, SIRAP TCP, SPORTident serial, SPORTident Center, ROC, MQTT, TmF Radio |
| Processors | Passthrough, Deduplicate, Enrichment |
| Data providers | Oribos data, shared by multiple enrichment processors |
| Targets | Formatted TCP client/server, File, HTTP, SIRAP, Oribos, OResults, ESC/POS USB printer |

The printer target requires Windows. Device drivers, serial hardware and the native Photino window need verification on the operator's installation. Automated tests cover protocol fixtures, local sockets, runtime changes and the Chromium UI.

## Configuration files

Flow documents are ordinary JSON files that can be copied and opened on another computer. They include nodes, connections, reusable filters and editor positions. Writes use atomic replacement and keep the previous file as `.bak`. Concurrent or external edits produce a conflict instead of overwriting changes.

`RadioSender/appsettings.json` contains only application settings: the local HTTP address (`Urls`), whether to show the desktop window (`Desktop:Enabled`) and logging (`Serilog`). Source/target settings belong to flow documents. The former appsettings configuration is not imported; create a new flow for this version. `appsettings.complex.example.json` is retained only as a historical topology reference.

The former Punches, Graph, Stats and Log pages, global UI target, Hangfire dashboard and standalone ManualSender have been retired. Inspection and operational controls now belong to the running nodes. Deliveries use bounded runtime queues. HTTP targets make up to three automatic retries for transient failures within the five-second delivery deadline; final failures can be inspected and retried explicitly.

## Build and run

Development requires .NET 10 and Node.js 24. The frontend uses Vue 3, TypeScript, Vuetify and Vue Flow; the runtime and JSON format are independent of these libraries. Frontend assets are built and embedded by the application project.

```sh
dotnet build RadioSender/RadioSender.csproj
dotnet run --project RadioSender/RadioSender.csproj
```

For local browser operation:

```sh
dotnet run --project RadioSender/RadioSender.csproj -- --Desktop:Enabled=false --urls=http://127.0.0.1:8082
```

Open `http://127.0.0.1:8082`. Configuration APIs are local to the RadioSender computer. Node.js is needed for building, not for running the compiled application.

```sh
dotnet test Test.RadioSender/Test.RadioSender.csproj
cd RadioSender/ClientApp
npx playwright install chromium
npm run test:e2e
```

Alternatively, set `PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH` to an installed Chromium executable. Browser tests use a separate server on port 18082 and temporary files. For frontend development, `npm run dev` proxies to the application on port 8082.

## Output formats

TCP, HTTP, File and Printer modules accept placeholders. For example, `{CompetitorId};{Control};{Time:HH:mm:ss.fff};{Cancellation}{CRLF}` includes the identifier, control, time and cancellation marker.

| Data | Placeholders |
| --- | --- |
| Identity | `{CompetitorId}`, `{CompetitorIdType}`, `{Bib}`, `{Card}`, `{Card2}` |
| Enrichment | `{Name}`, `{Class}`, `{Nation}`, `{Club}`, `{ClubName}`, `{ClubId}`, `{ClubNation}`, `{StartTime}` |
| Event | `{Control}`, `{ControlType}`, `{Type}`, `{Source}`, `{Status}`, `{Cancellation}` |
| Time | `{Time}`, `{ReceivedAt}`, `{UnixS}`, `{UnixMs}`, `{NetTime}` |
| Line endings | `{CRLF}`, `{CR}`, `{LF}` |

`{Bib}` and `{Card}` are populated only when known from the source or enrichment. Time placeholders accept .NET format specifiers. Check the source's net-time flag and the receiver's expectations before choosing a format.

A format without `{Cancellation}` suppresses cancellations. With the placeholder, cancellations use the existing `ANN` marker. Status events need `{Status}` or `{Time}`: without an explicit status field, the formatter uses sentinel times (`00:00:01` for DNS through `00:00:05` for OverTime). A successful socket write or print submission does not acknowledge receipt or physical printing; consult the node's Delivery inspector.

## Further reading

- [User and module-development guide](docs/flow-editor.md)
- [Architecture and incremental plan, in Italian](docs/configurazione-a-nodi.md)
- [Repository](https://github.com/EdoardoTona/RadioSender) and [releases](https://github.com/EdoardoTona/RadioSender/releases)

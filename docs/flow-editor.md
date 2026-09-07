# Flow configuration

Increments 1–5 of the application architecture are implemented. All existing input/output protocols have graph modules, with explicit deduplication and shared Oribos enrichment. Flow is the only application UI: the standalone ManualSender, global UI target, legacy pages and SignalR hub, dispatcher orchestration and Hangfire services have been removed. Protocol decoders, encoders and Oribos lookup rules remain shared with regression tests. Windows/macOS packaging and deployment changes are outside this cleanup.

## Create and run a flow

1. Start RadioSender. **Flow** is the home screen. Choose **New**, select an unused JSON file name, and enter **Editor**. **Open** loads an existing graph document into Flow without starting it.
2. Add modules from the library. Select each node to set its name, connection options or output file path. Drag from an output port on the right to an input port on the left to connect nodes. One output can connect to several inputs.
3. Select a connection to enable it, choose or create a reusable named filter, or set a delay in milliseconds. The graph displays the filter name and delay on the branch. Editing a shared filter changes every connection that references it; the editor shows the usage count.
4. Changes autosave after 650 ms. **Save** flushes immediately. **Save As** creates a copy and makes it the current document. New and Save As require an unused destination. Relative target paths are resolved against the configuration file's directory.
5. Choose **Apply**, review the affected nodes, then confirm. RadioSender activates the saved revision and returns to **Flow**. Changing only filters, edges, names or positions keeps unchanged module instances running. Changing a module's settings restarts that instance.
6. In Flow, select a node to use its controls, inspect its stream or read its logs. Select Manual input and use **Send event** to publish an event through its outgoing connections. Choose **Edit** to return to configuration.

Opening another document keeps the existing runtime active until Stop or Apply. The status bar identifies the running file and revision. Flow displays the applied graph while that document runs; the Editor contains the saved draft. Restarting the application shows the welcome screen and does not automatically run a document. Reloading a browser while a flow is active reconnects to that runtime.

In Photino, Open/New/Save As use native dialogs. In a local browser, enter an absolute JSON path on the RadioSender computer. The header's connection badge refers to the browser's connection to RadioSender and is hidden in Photino. No external fonts, icon services or CDNs are required at runtime.

## Inspection, replay and logs

- Source/provider output shows events before outgoing filters. Processor **Processing** shows suppressed events and processing failures; expand a competitor name to inspect enrichment fields. Passthrough offers separate Input and Output views. Target Input shows mapped data; Delivery shows the actual write result or error. History stays stable during live status updates. Pause and search affect only the displayed view.
- **Replay output** reinjects selected stored output into the current downstream graph. It does not repeat mappings before the selected node. **Retry target** sends stored input only to that target, without repeating its incoming filter.
- Replays require the current runtime session and revision. Expired selections and revision changes return an explicit error. Retrying the same replay request uses an operation ID to avoid duplicate acceptance within the session. TCP `Written` means the socket accepted bytes; it is not a receiver acknowledgement.
- **Node logs** includes messages carrying that node's ID and runtime session. **Logs** in the header shows general application messages; **Include node logs** combines both. Module state changes, failed deliveries, commands, replay and delayed-event rejection are recorded.
- Inspection retains up to 500 observations per node/direction, 10,000 total and approximately 16 MB. General/node logging retains 5,000 entries, with 500 displayed per query. This history is in memory and is separate from the JSON document.

A connection applies its filter before its delay. Delay is 0–60,000 ms, measured per accepted event; a burst of events does not accumulate one full delay per event. Each delayed edge has its own FIFO queue. Immediate branches continue independently. Target and delay queues are bounded to 256 waiting events, with a one-second acceptance timeout and explicit rejection. Target sends have a five-second cancellation deadline. TCP servers accept up to 64 simultaneous clients and input lines up to 8,192 characters.

Apply waits up to 15 seconds plus the longest configured edge delay for accepted delayed events and target deliveries before switching revisions. If draining times out, the current configuration remains active. Stop cancels events still waiting on delayed edges and records the cancellation in the originating node's logs. It gives target queues up to 15 seconds to drain, then cancels remaining sends with an explicit delivery record. A failed module start attempts to restore the previous graph; any restore failure is displayed explicitly.

## Protocol modules

| Category | Modules |
| --- | --- |
| Sources | Manual, formatted TCP client/server, Microgate REI2 TCP/serial, Microplus UDP, OBR UDP, SIRAP TCP, SPORTident serial, SPORTident Center, ROC, MQTT, TmF Radio |
| Processors | Passthrough, Deduplicate, Enrichment |
| Providers | Oribos data |
| Targets | Formatted TCP client/server, File, HTTP, SIRAP, Oribos, OResults, ESC/POS USB printer |

Microgate uses serial when `portName` is set, otherwise TCP `address` and `port`. MQTT supports topic lists, SPORTident/TmF payload selection, protocol version, TLS/WebSocket and authentication settings. ROC and SPORTident Center retain their existing polling/cursor semantics. Decoder callbacks enter a bounded 256-event queue; overflow is logged on that node. There is no implicit dispatcher deduplication or enrichment in these adapters.

HTTP, OResults and Oribos deliveries use the running target's queue and cancellation token. Transient failures (network errors, HTTP 408, 429 and 5xx) receive up to three automatic retries after the initial attempt, with waits of 250, 500 and 1,000 ms. A longer `Retry-After` header is respected. All attempts and waits share the existing five-second delivery deadline, so fewer attempts may run if that deadline expires. Other HTTP errors and a missing Oribos acknowledgement fail immediately. Retry attempts appear in the node logs; the Delivery inspector records one final result and the attempt count for completed HTTP responses or exhausted transport retries. Review failures and use **Retry target** from the target's Input view when appropriate. Apply drains accepted requests at the old destination before replacing it. HTTP errors produce `Failed`; Oribos also requires its `Ok` acknowledgement. Events that a protocol cannot represent produce `Suppressed`. SIRAP cannot represent cancellations, status changes or net-time events. OResults needs a card identifier, resolved by enrichment when available.

Automatic retries keep the original event and destination and recreate the request body for each attempt, including POST requests. Other branches continue independently. As with manual retry, a lost response can cause an event to be delivered more than once if the receiver already processed it. These retries handle brief interruptions; they do not provide a persistent delivery backlog or exactly-once delivery.

The ESC/POS target requires Windows and a configured USB printer. It submits to the Windows spooler; `Submitted` is not proof of physical printing. Native printer calls may not respond to cancellation until the driver returns. Serial hardware, printer output and Photino WebViews still need verification on the operator's Windows/macOS installations; automated tests use protocol fixtures, local sockets and Chromium.

## Deduplication and replay

Place **Deduplicate** where streams should share history. Identity includes identifier and type, control and type, event time, source ID, competitor status and net-time flag; reception time and enrichment fields do not affect it. Independent sources with different `SourceId` values remain distinct. A duplicate with the same cancellation state is suppressed. Cancellation, restoration and another cancellation each pass through in order.

Each instance remembers at most `capacity` identities (default 100,000). Oldest accepted identities are evicted at capacity; `retentionSeconds` optionally expires them (0 disables expiry). Repeated duplicates do not extend retention. Changing this node's settings, replacing the document, or restarting the flow clears its history. Applying only edge/layout changes keeps it. Replay bypasses deduplication without reading or altering live state, so intentionally replaying an old punch cannot undo a remembered cancellation.

## Shared Oribos data and radio controls

Add one **Oribos data** provider per server configuration. Select it in each **Enrichment** node's Provider field; do not connect a data edge to establish a lookup reference. Disabled, removed or wrong-type providers fail Apply validation. Each provider owns one polling loop and an atomic competitor snapshot, shared by all referring processors. Refresh failures retain the last successful snapshot. Before the initial load, unmatched punches pass through unchanged; check the provider's Loading/Disconnected state before relying on enrichment.

Enable **Emit status changes** to publish supported transitions on the provider's ordinary `out` port. Connect it to the desired status-processing branch. The initial snapshot does not replay historical statuses. Subsequent transitions reuse the existing Oribos rules, including sub-judice handling and finish-time corrections. **Reload data** schedules a refresh on that instance; repeated requests share a pending refresh, and Apply/Stop cancels and joins its work. A provider response is limited to 16 MB.

Select a running **TmF Radio** node in Flow to use **Ping radios** or open **Radio network**. The view shows nodes, links, signal and latency observed by that gateway, with limits of 256 nodes and 512 links. It is independent for every gateway. Ping requests status/path data; the command result does not claim that every remote radio has replied.

## JSON examples

Copy an example to a writable working directory, then Open it:

| Document | Behavior |
| --- | --- |
| [manual-branches.radiosender.json](../examples/manual-branches.radiosender.json) | Manual → named mapping 35→1 and 150 ms delay → Passthrough → File, plus an unchanged File branch |
| [manual-tcp.radiosender.json](../examples/manual-tcp.radiosender.json) | Same graph, with an additional TCP server target on port 12000 |
| [dedup-branches.radiosender.json](../examples/dedup-branches.radiosender.json) | Manual → Deduplicate → mapped/delayed File branch, plus an unchanged raw archive; input and expected outputs in [dedup-events.json](../examples/fixtures/dedup-events.json) |
| [oribos-shared.radiosender.json](../examples/oribos-shared.radiosender.json) | Two enrichment processors sharing Oribos, a status branch, and optional disabled OBR/SIRAP nodes |
| [tcp-branches.radiosender.json](../examples/tcp-branches.radiosender.json) | TCP server source on port 12001 instead of manual entry |

The deduplication fixture sends the same card/time/control with cancellation flags `false, false, true, true, false`. The raw archive receives five rows; the mapped archive receives three (normal, cancellation, restoration). `{Cancellation}` retains the existing `ANN` wire-format marker. `TestFlowPhase4` executes this example and compares both output files.

For the Oribos example, configure the provider host before Apply. [oribos-race.json](../examples/fixtures/oribos-race.json) is synthetic sample data: cards 1234/5678 resolve to bib 101 and Alex Runner. Enable the optional protocol nodes only after setting their connection details.

For TCP output, connect a receiver before sending (`nc 127.0.0.1 12000`). For TCP input, connect to port 12001 and send a UTF-8 line such as `123;35;12:34:56,123` followed by a newline. The mapped branch receives control 1 and the raw branch receives 35.

Documents use `schemaVersion: 1`, stable node/edge IDs, `filters` containing `{ id, name, rules }`, and edges referencing `filterId` plus `delayMs`. Each node has typed module settings represented as a JSON object, allowing incomplete drafts to autosave. Apply validates settings, ports, filter references and the acyclic graph. Unknown edge fields are rejected so routing rules cannot silently disappear. The earlier experimental inline `edge.filter` shape must be rewritten using `filters` and `filterId`; there is no legacy appsettings importer.

Writes use a temporary file and atomic replacement with a `.bak` copy of the previous document. The backend checks both document revision and disk hash. An external edit or another editor's update produces a conflict while the current UI keeps its draft. Reload explicitly discards that draft; Save As recovers it into a new file.

## Extending a module

- Add a settings record with `Display`, `Required`, `Range` and related validation attributes. `ModuleDefinition<TSettings>` generates defaults and field descriptors for the UI. The form supports strings, passwords, nullable integers, Booleans, enums and row editors for lists. Provider references use a node selector. Keep filter rules and enablement out of module settings: edges and nodes own those concerns. Obsolete properties are excluded from generated forms.
- Derive from `FlowModule`. Constructors must not open resources. Acquire resources in `StartAsync`, stop background work and close resources in `StopAsync`, and make disposal safe after partial startup. Sources publish through `ModuleContext.Output`; targets implement `SendAsync` and return a precise `DeliveryResult`. Respect cancellation and keep background work bounded.
- Register one factory in `ModuleRegistry.CreateDefault`. The runtime owns each instance and routes data by node ID. The existing `Filter.Transform` handles branch mappings without mutating the original `Punch`.
- Declare optional `ModuleCommand` entries and implement `ExecuteCommandAsync`. Commands are checked against the node descriptor, session and revision. A `CommandResult` can return output events for routing; simple actions are rendered as buttons automatically. ManualFlowModule is the first example.
- Declare an optional view key and register its Vue component in `ClientApp/src/modules/views.ts`. The operational component receives node ID, runtime and disabled state. It invokes `/api/flow/nodes/{id}/commands/{command}` with `{ sessionId, revision, arguments }`. Configuration fields remain in Editor.
- Use the module's protected `Logger` for scoped messages. `SetState` also records state changes automatically. TmF exposes **Radio network** as an instance view. `GET /api/flow/nodes/{id}/view?sessionId=...&revision=...` returns its bounded snapshot; retired or stale instances are rejected. There is no global Graph page or shared radio-command broadcast.

Protocol adapters can reuse a decoder through `ProtocolSourceModule` and `IDispatchSink`, which provide instance-scoped logging, state and bounded event publication. Decoders do not resolve filters or destinations. Configuration records describe protocol settings; no per-protocol appsettings registration or global service is needed.

The runtime and document format have no dependency on Vue Flow or Vuetify. UI positions and viewport live separately under `editor`. `Runtime/FlowRuntime.cs` owns apply/stop/replay ordering; `TargetQueue` and `DelayedEdgeQueue` own bounded asynchronous delivery; `FlowJournal` and `FlowLogs` own inspection history; `Configuration/FlowDocuments.cs` owns persistence.

## Build and verify

`appsettings.json` configures only `Urls`, `Desktop:Enabled` and `Serilog`. Graph documents own every source, target and processing rule. The old `appsettings.complex.example.json` remains a historical reference, not an openable flow.

Use .NET 10 and Node.js 24. `dotnet build RadioSender/RadioSender.csproj` restores and builds the frontend when needed and embeds the generated assets, including clean publishes. Node.js is needed only during development/build, not on an operator's computer.

```sh
dotnet build RadioSender/RadioSender.csproj
dotnet test Test.RadioSender/Test.RadioSender.csproj
cd RadioSender/ClientApp
npm run test:e2e
```

Install Playwright Chromium once with `npx playwright install chromium`, or set `PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH` to an existing compatible executable. Browser tests run a separate local server on port 18082 with `Desktop:Enabled=false` and temporary configuration/output files.

For headless operation, run `dotnet run --project RadioSender/RadioSender.csproj -- --Desktop:Enabled=false --urls=http://127.0.0.1:8082`. During frontend development, `npm run dev` proxies APIs and SignalR to that address. Flow APIs are restricted to loopback requests with matching Host/Origin; mutation requests also require `X-RadioSender-Client: flow-editor`. Remote administration and authentication are outside these increments.

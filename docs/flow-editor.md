# Flow configuration

The first three increments of the graph architecture are implemented. Available modules are Manual input, TCP input, Passthrough, TCP output and File output. Other protocol adapters, enrichment and deduplication are still scheduled for increment 4. The old protocol implementations and their tests remain in the repository; the application no longer starts their instances from `appsettings.json`.

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

- Source output shows events before outgoing filters. Passthrough offers separate Input and Output views. Target Input shows mapped data; Delivery shows the actual write result or error. History stays stable during live status updates. Pause and search affect only the displayed view.
- **Replay output** reinjects selected stored output into the current downstream graph. It does not repeat mappings before the selected node. **Retry target** sends stored input only to that target, without repeating its incoming filter.
- Replays require the current runtime session and revision. Expired selections and revision changes return an explicit error. Retrying the same replay request uses an operation ID to avoid duplicate acceptance within the session. TCP `Written` means the socket accepted bytes; it is not a receiver acknowledgement.
- **Node logs** includes messages carrying that node's ID and runtime session. **Logs** in the header shows general application messages; **Include node logs** combines both. Module state changes, failed deliveries, commands, replay and delayed-event rejection are recorded.
- Inspection retains up to 500 observations per node/direction, 10,000 total and approximately 16 MB. General/node logging retains 5,000 entries, with 500 displayed per query. This history is in memory and is separate from the JSON document.

A connection applies its filter before its delay. Delay is 0–60,000 ms, measured per accepted event; a burst of events does not accumulate one full delay per event. Each delayed edge has its own FIFO queue. Immediate branches continue independently. Target and delay queues are bounded to 256 waiting events, with a one-second acceptance timeout and explicit rejection. Target sends have a five-second cancellation deadline. TCP servers accept up to 64 simultaneous clients and input lines up to 8,192 characters.

Apply waits up to 15 seconds for accepted delayed events and target deliveries before switching revisions. If draining times out, the current configuration remains active. Stop cancels events still waiting on delayed edges and records the cancellation in the originating node's logs. It gives target queues up to 15 seconds to drain, then cancels remaining sends with an explicit delivery record. A failed module start attempts to restore the previous graph; any restore failure is displayed explicitly.

## JSON examples

Copy an example to a writable working directory, then Open it:

| Document | Behavior |
| --- | --- |
| [manual-branches.radiosender.json](../examples/manual-branches.radiosender.json) | Manual → named mapping 35→1 and 150 ms delay → Passthrough → File, plus an unchanged File branch |
| [manual-tcp.radiosender.json](../examples/manual-tcp.radiosender.json) | Same graph, with an additional TCP server target on port 12000 |
| [tcp-branches.radiosender.json](../examples/tcp-branches.radiosender.json) | TCP server source on port 12001 instead of manual entry |

For TCP output, connect a receiver before sending (`nc 127.0.0.1 12000`). For TCP input, connect to port 12001 and send a UTF-8 line such as `123;35;12:34:56,123` followed by a newline. The mapped branch receives control 1 and the raw branch receives 35.

Documents use `schemaVersion: 1`, stable node/edge IDs, `filters` containing `{ id, name, rules }`, and edges referencing `filterId` plus `delayMs`. Each node has typed module settings represented as a JSON object, allowing incomplete drafts to autosave. Apply validates settings, ports, filter references and the acyclic graph. Unknown edge fields are rejected so routing rules cannot silently disappear. The earlier experimental inline `edge.filter` shape must be rewritten using `filters` and `filterId`; there is no legacy appsettings importer.

Writes use a temporary file and atomic replacement with a `.bak` copy of the previous document. The backend checks both document revision and disk hash. An external edit or another editor's update produces a conflict while the current UI keeps its draft. Reload explicitly discards that draft; Save As recovers it into a new file.

## Extending a module

- Add a settings record with `Display`, `Required`, `Range` and related validation attributes. `ModuleDefinition<TSettings>` generates defaults and field descriptors for the UI. The initial form supports string, integer and Boolean settings.
- Derive from `FlowModule`. Constructors must not open resources. Acquire resources in `StartAsync`, stop background work and close resources in `StopAsync`, and make disposal safe after partial startup. Sources publish through `ModuleContext.Output`; targets implement `SendAsync` and return a precise `DeliveryResult`. Respect cancellation and keep background work bounded.
- Register one factory in `ModuleRegistry.CreateDefault`. The runtime owns each instance and routes data by node ID. The existing `Filter.Transform` handles branch mappings without mutating the original `Punch`.
- Declare optional `ModuleCommand` entries and implement `ExecuteCommandAsync`. Commands are checked against the node descriptor, session and revision. A `CommandResult` can return output events for routing; simple actions are rendered as buttons automatically. ManualFlowModule is the first example.
- Declare an optional view key and register its Vue component in `ClientApp/src/modules/views.ts`. The operational component receives node ID, runtime and disabled state. It invokes `/api/flow/nodes/{id}/commands/{command}` with `{ sessionId, revision, arguments }`. Configuration fields remain in Editor.
- Use the module's protected `Logger` for scoped messages. `SetState` also records state changes automatically. The future TmF adapter should expose **Radio network** as its own instance view here; the old global Graph page is not linked from the new UI.

The runtime and document format have no dependency on Vue Flow or Vuetify. UI positions and viewport live separately under `editor`. `Runtime/FlowRuntime.cs` owns apply/stop/replay ordering; `TargetQueue` and `DelayedEdgeQueue` own bounded asynchronous delivery; `FlowJournal` and `FlowLogs` own inspection history; `Configuration/FlowDocuments.cs` owns persistence.

## Build and verify

Use .NET 10 and Node.js 24. `dotnet build RadioSender/RadioSender.csproj` restores and builds the frontend when needed and embeds the generated assets, including clean publishes. Node.js is needed only during development/build, not on an operator's computer.

```sh
dotnet build RadioSender/RadioSender.csproj
dotnet test Test.RadioSender/Test.RadioSender.csproj
cd RadioSender/ClientApp
npm run test:e2e
```

Install Playwright Chromium once with `npx playwright install chromium`, or set `PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH` to an existing compatible executable. Browser tests run a separate local server on port 18082 with `Desktop:Enabled=false` and temporary configuration/output files.

For headless operation, run `dotnet run --project RadioSender/RadioSender.csproj -- --Desktop:Enabled=false --urls=http://127.0.0.1:8082`. During frontend development, `npm run dev` proxies APIs and SignalR to that address. Flow APIs are restricted to loopback requests with matching Host/Origin; mutation requests also require `X-RadioSender-Client: flow-editor`. Remote administration and authentication are outside these increments.

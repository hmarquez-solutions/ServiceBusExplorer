# Phase 3 backlog: getting the new app to parity

This continues [PROPOSAL.md](./PROPOSAL.md). Phases 1 and 2 are built:

- **Phase 1:** environment tags and Prod-rule confirmations in the WinForms app.
- **Phase 2:** a read-only Avalonia slice in `src/ServiceBusExplorer.Desktop`.

Each item below is sized to be one pull request. They're roughly in priority order; items 1 and 2 come before anything that changes messages.

## Ground rules for every item

- **Prod rules apply to every write.** Prod and Untagged connections need the entity name typed to confirm a purge, a delete or a bulk action. Item 2 builds this once; nothing that writes ships before it.
- **Talk to Azure only through `INamespaceClient`.** Add methods there and keep the view models testable with `FakeNamespaceClient`.
- **Read before you write.** Peek is the default everywhere. A destructive mode is something you switch to on purpose, and it looks different.
- **Done means tested.** Each item adds view-model tests, plus a manual check in `TESTING.md` for anything that needs a live namespace.

## Items

1. **Saved connections.** Pasting a connection string every time stops daily use.
   - Read the WinForms saved connections and their environment tags from the WinForms config: the `serviceBusNamespaces` section, plus the `connectionEnvironment:<key>` appSettings added in Phase 1. Both apps then share one list.
   - The values are the WinForms format. SAS entries are ordinary connection strings. Entra entries look like `Endpoint=...;AuthMode=AAD;TenantId=...`; see `ServiceBusNamespace` in `src/Common/Helpers`.
   - Start read-only. Writing back to that file, and protecting secrets with DPAPI (Windows) or the OS keychain, can follow.
2. **Typed-confirmation dialog.** One reusable dialog showing the environment band, an impact summary and a type-the-name box, as in the mockup's Purge dialog. It's required under Prod rules and optional otherwise. Unit-test the rule itself; port it from `ConnectionEnvironmentHelper.RequiresStrictConfirmation`.
3. **Send message.** Body editor, content type, application properties, and optional scheduled enqueue time.
4. **Resubmit and Edit & resubmit from the dead-letter queue.**
   - Order matters: first receive the original from the DLQ in PeekLock and check it's the selected one (by sequence number). Then send the copy to the source entity. Complete the original only after the send succeeds.
   - If the send fails, abandon the original. If the complete fails after a successful send (for example, the lock expired), the copy already went out: report that clearly rather than retrying.
   - There's no receive-by-sequence-number for active or DLQ messages, so reaching the selected one means receiving the messages in front of it. **Keep those locked until the target is found** (abandoned messages come straight back and get received again), bounded by a count and by the lock duration. Then abandon them.
   - Every abandon raises a message's delivery count. Messages in the DLQ stay there: they can't be dead-lettered again. Show how many messages are in front of the selection before acting.
5. **Purge** a queue, a subscription or a DLQ. Reuse `QueueServiceBusPurger` and `TopicSubscriptionServiceBusPurger` from `src/ServiceBus`. That library is netstandard2.0 and references `Azure.Messaging.ServiceBus` 7.19.0, while the Desktop app uses 7.21.0. Referencing it from Desktop resolves to 7.21.0, so re-run the purge tests against that version when adding the reference.
6. **Delete selected messages.** It reaches messages the same way as item 4: preceding messages stay locked until the targets are found. Then complete the targets and abandon the rest. In the **active** queue, an abandon that reaches the entity's max delivery count dead-letters that message, so warn before deleting from the active queue. In the DLQ, abandons only raise the delivery count.
7. **Create, edit and delete entities** with `ServiceBusAdministrationClient`.
   - Durations show as value plus unit ("never expires"), as in the mockup's Settings tab.
   - Creation-only properties (partitioning, sessions, duplicate detection) are read-only after creation.
   - An unsaved-changes bar shows the diff before Update.
8. **Subscription rules.** List, add and remove SQL or correlation filters, with optional actions.
9. **Session browsing.**
   - SDK 7.21 can list sessions: `ServiceBusClient.GetMessageSessionsAsync`, with overloads for queues and subscriptions. Peeking a session means `AcceptSessionAsync`.
   - **Accepting a session takes its lock**, which blocks the real consumer of that session until the lock is released. Hold it briefly, close it straight away, and warn under Prod rules. That's the one place where browsing isn't side-effect free.
10. **Command palette (Ctrl+K) and Activity drawer**, as in the mockup.
    - The palette: jump to an entity, or run any command available for the selection.
    - The drawer: a one-line latest event, plus warnings and errors.
11. **Export and import.** Messages to and from JSON or NDJSON; entity definitions to and from the WinForms export format.
12. **Other entity types.** For each, the first PR is a read-only browse, like Phase 2: listed in the navigator with its key properties shown in the header, and tests through a fake. Writes come after, following the ground rules.
    - **Event Hubs:** hubs with partition count and consumer groups, plus per-partition sequence ranges. `src/EventHubs` is already netstandard2.0.
    - **Relays:** relays with type and listener count. `src/Relay` is already netstandard2.0.
    - **Event Grid:** topics and their event subscriptions. `src/EventGridExplorerLibrary` is already netstandard2.0.
    - **Notification Hubs:** hubs and their registrations. `src/NotificationHubs` is net472 on `Microsoft.Azure.NotificationHubs` 1.0.9, so it first needs a package version that targets .NET Standard or .NET.
13. **Ship it.**
    - A publish workflow for the Desktop app: self-contained, single-file Windows x64 build attached to releases. macOS and Linux builds can come later.
    - A version stamp shared with the WinForms app.
    - A "try the new app" link in the WinForms Help menu.

## Retiring WinForms

When the team hasn't opened the WinForms app for a sprint:
- Stop shipping it.
- Remove `src/Common` and the other net472 projects.
- Drop the legacy `WindowsAzure.ServiceBus` package.

# Service Bus Explorer: UI redesign proposal

Mockup: [`mockup.html`](./mockup.html) (open it in a browser). Press the **?** button in the title bar to show annotations, **Ctrl+K** for the command palette, click the connection pill to cycle PROD / STAGING / DEV, and try **Purge…** in each environment.

## The problem

The tool is very capable, but the UI doesn't help you use it. In the current UI:

- **Everything is a modal or a menu.** `MainForm.Designer.cs` declares about 200 menu items. To read messages you click Messages or Deadletter, pick Peek / Receive and Delete / Top N in the `ReceiveModeForm` modal, and only then does a results tab appear (grid + body + `PropertyGrid`). Repairing or resubmitting a message opens a separate `MessageForm` pop-up.
- **Counts are buried.** Active and dead-letter counts appear as `(0, 0)` suffixes in the tree, or in a name/value property grid on the queue's Description tab.
- **Durations are unreadable.** Each TimeSpan is five boxes (days, hours, minutes, seconds, ms). An infinite TTL shows as `10675199 / 2 / 48 / 5 / 477`.
- **Nothing tells you which environment you're in.** A prod namespace looks the same as dev. Purge and Receive & delete look the same everywhere.
- **The log pane is all or nothing.** It is open by default and takes about a quarter of the window, mostly for success messages. View → Log Window hides it completely, and warnings disappear with it.
- **It's light-only, fixed-layout WinForms with 2015-era iconography.**

## Design principles

1. **The data is the UI.** Counts, DLQ reasons, and message bodies show inline. They don't hide behind buttons and dialogs.
2. **Safe by default, deliberate when dangerous.** Peek is the default read mode. Destructive actions are red, say exactly what they'll do, and require typing the entity name in PROD.
3. **Environment is always visible.** Each saved connection gets an environment tag. It colors the window's top band and the confirm dialogs.
4. **Keyboard first.** Ctrl+K jumps to any entity or runs any command, replacing deep context menus for power users. Arrow keys move through messages.
5. **Native look.** Segoe UI Variable, Cascadia Mono, and Windows 11 accent colors, so the result could be built in WPF or WinUI without fighting the toolkit.

## What's in the mockup

| Area | Today | Proposed |
|---|---|---|
| Navigator | Tree with `(active, dlq)` text suffixes | Tree with count badges (DLQ in red), filter chips (Has DLQ, Pinned), pinned entities |
| Entity header | Panel title `View Queue: x` | Name, status, feature tags, and live counts for Active, DLQ, Scheduled, Transfer DLQ, and Size |
| Messages | Button → mode modal → results tab (grid + body + property grid); a pop-up to repair or resubmit | Messages / Dead-letter / Scheduled tabs that exist before you load anything, an inline Peek ↔ Receive & delete switch, and an inspector with Body / Properties / System tabs plus inline Resubmit and Edit & resubmit |
| Dead-letter | Separate "Deadletter" button | Its own tab with a count badge, the reason in a column, and the reason and description in the inspector |
| Bulk ops | Context menu on a selection | A selection bar: Resubmit / Export / Delete |
| Settings | Five-box TimeSpans, flat checkbox list | Grouped cards; durations as value + unit ("never expires"); creation-only flags shown read-only; an unsaved-changes bar with a diff |
| Destructive actions | Generic confirm | Environment-aware dialog with impact summary; type-to-confirm in PROD |
| Log | Pane that is fully shown or fully hidden (View → Log Window) | Activity drawer that stays collapsed to one line showing the latest event and the warning count |
| Commands | ~200 menu items | Command palette (Ctrl+K), plus the context menus kept for discoverability |
| Theme | Light only | Light and dark |

Not mocked yet: Connect dialog, Send message, Event Hubs / Relays / Notification Hubs / Event Grid views, namespace dashboard. They would follow the same patterns.

## How to get there

This work happens in our fork (`hmarquez-solutions/ServiceBusExplorer`). We are not waiting on agreement from upstream.

### What the code allows

- The app and `Common` target **net472**. The data layer in `Common` (`ServiceBusHelper` plus about 50 files across `Common/Helpers`, `Common/WindowsAzure` and `Common/Abstractions`) is built on the legacy `WindowsAzure.ServiceBus` SDK, which runs only on .NET Framework. Another 36 files in the app's controls, forms, UI helpers and tests use it too, for 86 in total.
- A modern-SDK library already exists: `src/ServiceBus` (netstandard2.0, `Azure.Messaging.ServiceBus`, `ServiceBusHelper2`, the purgers). `src/Utilities` is also netstandard2.0.
- [Purple Explorer](https://github.com/philipmat/PurpleExplorer) shows that .NET 10, Avalonia 12 with its Fluent theme, and `Azure.Messaging.ServiceBus` work together for this kind of app.

### Decision: a new Avalonia app next to the old one

We build a new app in **Avalonia on .NET 10**, written directly against `Azure.Messaging.ServiceBus` and `ServiceBusAdministrationClient`. The WinForms app keeps shipping until the new one does what the team needs, and it then retires.

- **No big migration first.** We don't port `Common` to the new SDK. Its legacy code gets *replaced* one feature at a time in the new app. Only the parts the new app needs get written, which removes the months-long migration phase from the earlier plan.
- **Why Avalonia, not WPF.** The cost is about the same. Its XAML and MVVM look like WPF, its Fluent theme gives light and dark modes, and it runs on Windows, macOS and Linux at no extra cost. Cross-platform isn't a goal, just a bonus.
- **Risk:** contributors have to learn Avalonia. It's close enough to WPF that this is a small cost.

### Phases

1. **Phase 1: safety in today's app (WinForms).** The prod risk exists now, and the new app takes time.
   - Each connection gets an **environment tag**: Dev, Test, Staging or Prod, picked from a dropdown in the Connect dialog. Changing it on a saved connection saves it straight away. When you save a new connection without picking a tag, it's tagged Prod if the namespace name contains `prod`, `prd` or `production`, and otherwise it stays Untagged.
   - **No tag means Prod rules.** An untagged connection, including an ad-hoc one that was never saved, gets the strict confirmations until someone tags it.
   - The tag shows in the title bar plus a colored band across the top of the window. Untagged connections show as "Untagged".
   - Under **Prod rules**, purging needs the entity name typed to confirm. Today it only asks yes/no.
   - Under Prod rules, the existing type-the-name check on deletes can't be turned off. Today one checkbox disables it everywhere.
2. **Phase 2: first slice of the new app.** A `src/ServiceBusExplorer.Desktop` project (Avalonia, net10.0) with tests.
   - Connect with a connection string or Entra ID.
   - A navigator tree with Active and DLQ counts and a filter.
   - An entity header with counts.
   - Messages / Dead-letter / Scheduled tabs that peek messages into the inspector (Body, Properties, System).
   - The environment band, and dark/light themes.
   - **Read-only on purpose:** this slice can't change or delete anything, so it's safe to point at Prod from day one.
3. **Phase 3: getting to parity (team backlog), roughly in this order.**
   - Send message, resubmit and edit-and-resubmit, purge and delete with environment-aware confirmation.
   - Create and edit entities, with human-readable durations.
   - Subscription rules, sessions, export/import.
   - Command palette, Activity drawer.
   - Sharing saved connections with the WinForms app.
   - Event Hubs, Relays, Event Grid, Notification Hubs.

   We stop building WinForms features as soon as Phase 2 lands. WinForms only gets fixes from then on.

## Open questions

- **Tagging friction.** Untagged connections get Prod rules, so every existing saved connection starts strict until it's tagged. That's deliberate, but it means a one-time tagging pass for each person.
- **Saved connections in the new app.** Read the WinForms config file, or start a new store? Either way, connection-string secrets need protecting: DPAPI on Windows, and the OS keychain if we ever ship on macOS or Linux.
- **When to retire WinForms.** Proposal: when nobody on the team has opened it for a sprint.

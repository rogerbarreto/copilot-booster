# 0002 — Copilot Host focus defaults to window-level; pane focus is opt-in per `HostKindLabel`

Status: Accepted (0.21.0)

## Decision

When a Copilot CLI runs inside a multi-pane host (Windows Terminal tabs/splits, multiplexers), Copilot Booster focuses the **Copilot Host** window only by default. Pane-level focus (selecting the specific tab or split that owns the Copilot CLI process) is implemented per `HostKindLabel` only where the host exposes a reliable mechanism. Dispatch lives behind `HostKindLabel`. As of 0.21.0 only `"Windows Terminal"` has a pane-focus implementation (UIA tab matching by title with a 250ms time-box, falling back to window-level focus on any failure).

## Why this matters

Each terminal/multiplexer would otherwise need bespoke automation (UIA, IPC, OSC sequences, keystroke injection). The maintenance cost of supporting them all greedily is high; the value of supporting `wt.exe` specifically is high because it's the dominant Copilot Booster host.

## Considered alternatives

- **No pane focus anywhere.** Cleaner code, but multi-tab WT users see Booster select the wrong tab too often. Rejected.
- **Pane focus for every host via keystroke injection (Ctrl+Tab cycling).** Brittle (user keybindings vary), visible side effects, no guarantees of correctness. Rejected.

## Consequences

- `IWindowsTerminalPaneGateway` interface + a UIA-based implementation are introduced; unit tests fake the gateway.
- Adding pane focus for a new host is a localized change behind the `HostKindLabel` switch.
- Pane focus is best-effort: any UIA exception, time-box exceeded, or unresolved Booster-Resolved Name falls back to window-level focus silently.

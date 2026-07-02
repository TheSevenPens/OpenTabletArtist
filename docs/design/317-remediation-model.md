# 317 — Config / setup remediation model

**Status:** Phase 1 landed; phases 2–3 planned
**Issue:** #317

## Goal

Give the user an experience that surfaces the right information and, when something needs action,
points them to where the fix is made. Concretely:

- **Home shows only what needs attention.** When everything's healthy, Home is quiet.
- **Warning/remediation cards** describe a problem and offer a **Fix** — but the fix doesn't have to
  happen on Home; the card *directs* the user to where it lives.
- **The same issue can appear in more than one place** — on Home and locally at the top of the page
  that owns the fix.
- **Three severity tiers:** *Broken* (a prerequisite is missing; core function won't work),
  *Misconfigured* (set up wrong; a feature won't behave), *Recommendation* (works, but not ideal).
- **Re-validate periodically**, because OTD's own UX can change settings underneath us.

The motivating example: the Windows Ink plugin must be **installed** *and* **enabled** — two criteria,
each with its own evolving warning.

## Architecture

Pure, testable core in `Domain/Health/Health.cs`:

- `HealthSeverity` = Broken | Misconfigured | Recommendation (sorts worst-first, drives the dot color).
- `HealthIssue(Id, Severity, Title, Detail, Remediation?)` — `Id` is a stable key (dedupe + tests).
- `Remediation(ActionLabel, Area, TabletName?)` — `Area` says where the fix lives; `TabletName` targets
  a specific tablet for per-tablet areas.
- `HealthInputs` (pure snapshot) → `HealthEvaluator.Evaluate` → ordered `IReadOnlyList<HealthIssue>`.

Live wiring in `Services/HealthService.cs` (an `ObservableObject`):

- Gathers a `HealthInputs` snapshot from `IConnectionState` + `IDeviceData` + `WindowsInkPluginService`.
- Re-evaluates on every daemon **DataLoaded** and connection-state change — plus a public `Refresh()`
  the app calls after an action that changes health state without a reload (e.g. installing the WinInk
  plugin), so the catalog updates immediately instead of on the next 30 s poll.
- Exposes `ObservableCollection<HealthIssue> Issues` + `HasIssues`, and `IssuesFor(area, tablet)` so a
  page can render just the issues whose fix lives on it (same shared source as Home).

Surfaces:

- **Home "Needs attention" stack** (`DashboardView`): all issues, worst-first, hidden when healthy.
  Each card = severity dot + title + detail + **Fix** button. `DashboardViewModel.Remediate` dispatches:
  daemon → Refresh/Restart in place; Windows Ink → navigate to the Windows Ink Plugin page; tablet →
  navigate to that tablet.
- The shared instance lives in `MainViewModel` and is handed to the pages that need it.

## Catalog (phase 1)

| Id | Severity | Fix area |
| --- | --- | --- |
| `daemon.missing` | Broken | Daemon |
| `daemon.disconnected` | Broken | Daemon |
| `winink.notInstalled` | Broken | Windows Ink page |
| `winink.versionMismatch` | Misconfigured | Windows Ink page |
| `vmulti.notInstalled` | Broken | VMulti page |
| `tablet.notWinInk:<name>` | Misconfigured | that tablet's Pen Behavior |
| `daemon.foreign` | Recommendation | Daemon |

The pen-pressure setup chain is **three** prerequisites, all independent so they surface at once when
missing: the **VMulti driver** (the virtual pen device), the **Windows Ink plugin**, and the tablet's
**Pen Behavior** set to a Windows Ink mode. The Windows Ink plugin injects pressure/tilt *through*
VMulti's virtual HID device — see OTD's own README ("Windows Ink … and VMulti system driver").

## Decisions / notes

- **Windows Ink and VMulti management moved to their own pages** (Advanced → *Windows Ink Plugin*,
  *VMulti Driver*), off Home. Home now just flags the issue and the Fix button navigates there — the
  direct-to-location model in action.
- **VMulti *is* a checked prerequisite** (corrected — an earlier draft wrongly excluded it). The Windows
  Ink plugin injects pen input through VMulti's virtual HID device, so a missing VMulti driver breaks
  pressure/tilt just like a missing plugin does. Its detection is async P/Invoke owned by the VMulti
  page, which pushes the result into `HealthService.SetVMultiInstalled(...)`; the input is nullable so
  no false "not installed" flashes before the first detection reports.
- **`daemon.disconnected` is suppressed while a connect is in flight**, so startup doesn't flash a
  scary "not connected" card.

## Deferred (phases 2–3)

- **Local-in-tab unified cards** — render the shared `IssuesFor(...)` cards at the top of the relevant
  page (replacing the bespoke local warnings like the Pen Behavior "needs Windows Ink / Fix"). The
  per-tablet issue already dual-surfaces via that existing local Fix, so this is a consistency pass.
- **Calibration-stale check** — needs the per-profile mapping-fingerprint plumbing currently living in
  `TabletDetailViewModel`; extract a shared helper first.
- **Fold `DriverConflictMonitor` into the framework** — it still renders as its own Home alert.
- **Deep-link to a specific inner tab** (e.g. Calibration) — not needed yet because the tablet issue's
  fix is on the default (Pen Behavior) tab; add a `SelectedTab` mechanism when calibration lands.

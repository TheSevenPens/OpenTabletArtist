# Design: "Pen isn't set up for drawing" health check

Status: **implemented** (#artist-pen-health). See also [317-remediation-model.md](317-remediation-model.md)
and `Domain/Health/Health.cs`.

## As built

- **Check** — `HealthEvaluator.AddTabletPenBehaviorIssues` fires per detected tablet as soon as **any one**
  offender (Windows Ink off, or pen tip / pressure / tilt disabled) is active — each individually hurts
  drawing enough to surface. Recommendation tier. It **absorbs** the standalone Windows-Ink-off FYI note (#549).
- **Card** — a per-tablet Home card with a review link per active offender (`HealthIssue.Links`, each a
  `HealthLink` → a `RemediationArea`), plus the primary **Restore recommended** Fix.
- **Deep-links** — new `TabletDetailTab.PenInputs` / `PenDynamics`, wired through `PenDetailView.SelectTab`;
  `RemediationArea.TabletPenInputs` / `TabletPenTilt` map to them in `DashboardViewModel.FollowLink`.
- **One-click restore** — `RemediationArea.RestorePenBehavior` → `AppSession.RestoreRecommendedPenBehaviorAsync`
  → pure `Domain.PenBehaviorRestore.ToRecommended` (Windows Ink on [Windows], tip restored, pressure + tilt
  re-enabled) in one apply. It **also clears the `WinInkAutoOptOut` flag** — the Windows-Ink-off offender
  reads that flag, not just the output-mode path, so switching the mode isn't enough on its own. No confirm
  dialog — it only re-enables recommended defaults, nothing destructive.
- **No dismiss** — it's a real Recommendation (not developer-induced), so like the other real warnings it
  isn't dismissible; it clears itself when the settings change.
- **Developer aids** — a `ForceArtistPenBehavior` toggle (synthetic sample tablet) renders the full card for
  review/screenshots, and a "Break the pen for drawing" button (`BreakPenForDrawing`) turns on all four
  offenders on the *active* tablet — a real, persisted change so the card and its Fix can be exercised end
  to end.
- **Tests** — `HealthEvaluatorTests` (threshold, absorb, two-offender links) + `PenBehaviorRestoreTests`.

## Original proposal

## Problem

Several pen settings, each individually legitimate, combine to make the tablet useless for an artist —
and there's no single place that surfaces or fixes them. A user can end up here by toggling options across
three different tabs and never realize why their strokes are flat or their pen does nothing:

| Setting | Where it's set | Effect when on |
|---|---|---|
| **Don't use Windows Ink** | Pen › movement | Apps get no pressure or tilt at all |
| **Disable pen tip** | Pen › inputs | Tapping the tablet does nothing |
| **Disable pressure sensitivity** | Pen › inputs | Flat strokes, no line weight |
| **Disable tilt** | Pen › dynamics | Tilt-reactive brushes don't respond |

Today only "Don't use Windows Ink" is surfaced at all — as a standalone **Information** note (#549). The
other three are silent. This check bundles all four into one prominent, artist-facing warning.

## Proposed UX

A single per-tablet **Home → Needs attention** card that:

- Lists **only the settings currently active** (0 rows → no card). Each row is one line: an icon, a short
  label, and a link to the exact tab that owns it (`Pen › movement`, `Pen › inputs`, `Pen › dynamics`).
- Leads with a **Restore recommended pen settings** action — the one thing that *can* be a single fix:
  it re-enables Windows Ink + tip + pressure + tilt on that tablet's profile in one `SetSettings` apply.
  This is the main payoff, since there is otherwise no unified fix.
- Offers a **Dismiss** (these can be deliberate — e.g. osu! players intentionally run flat/no-Ink).

Compact card (revised mockup): header row = warning icon + title + *Restore recommended*; below it a dense
hairline list of the active offenders, each linking to its tab. Recommendation-tier (amber) — the settings
all *work* and are user-chosen, so this is a recommendation, not Broken/Misconfigured.

## Why it's more complicated than existing checks

The current model (`Domain/Health/Health.cs`) assumes **one issue → one fix**:

- `HealthIssue` carries a single `Remediation(ActionLabel, RemediationArea, TabletName?)`.
- The Home card renders one title + detail + one **Fix** button that deep-links to one place.

This check breaks both assumptions: it's **one issue → many locations**, and the most useful action is a
**synthesized bulk fix** that no single tab exposes.

## Changes required

1. **Health model — sub-items.** Extend `HealthIssue` to optionally carry a list of sub-remediations
   (`{ Label, RemediationArea, TabletName }`), or introduce a distinct issue subtype with its own card
   template. The single-`Remediation` path stays for every other check.

2. **New deep-link targets.** `TabletDetailTab` currently only has `PenBehavior` and `DisplayMapping`. Add
   targets for **Pen › inputs** (tip + pressure sensitivity) and **Pen › dynamics** (tilt), and wire them
   through `PenDetailView` / `DynamicsView` (mirrors the existing `PenBehavior`/`DisplayMapping` deep-links).
   Note: pressure sensitivity now lives on Pen › inputs and tilt on the dynamics tab.

3. **Bulk-restore remediation.** A new `RemediationArea` (e.g. `RestorePenBehavior`) whose action, on the
   target tablet's profile, sets output mode back to a Windows Ink absolute mode, restores the tip binding,
   clears `DisablePressure`, and clears `DisableTilt`, then applies once. It's a multi-setting mutation, so
   it should confirm first and touch nothing else.

4. **Absorb the Windows-Ink-off note.** When this bundle fires, it should **replace** the standalone
   `WinInkOptedOut` Information note (#549) so the two don't overlap; keep the standalone note only when
   Windows Ink is the *only* offender (or drop it entirely in favor of this card — decide below).

5. **Inputs.** Add the four flags to `TabletHealthInput` (Windows Ink already partly modeled via
   `WinInkOptedOut`/`OutputModeIsWinInk`; add `PenTipDisabled`, `PressureDisabled`, `TiltDisabled`). The
   Dashboard already reads these off the profile when it builds the health snapshot.

## Open decisions

- **Firing threshold.** Any single offender, or only when it's genuinely drawing-hostile (Windows Ink off,
  or ≥2 offenders)? A lone "tilt off" probably shouldn't trigger the big card. *Recommendation: fire on
  Windows Ink off OR ≥2 offenders; a single non-Ink offender stays a quiet per-tab hint.*
- **Relationship to the WinInk-off Information note** — absorb (recommended) vs. coexist.
- **Dismiss semantics** — per-tablet, persisted like a "these are intentional" acknowledgement, and cleared
  when the settings change again? Or session-only?
- **Severity** — Recommendation (amber) as proposed; not Broken, since nothing is missing and it all works.

## Rough scope

Model sub-items + 2–3 new pen deep-link targets + 1 bulk-restore remediation + a card template + the
evaluator check and its unit tests. Medium — the deep-link wiring and the bulk-restore apply path are the
substantive parts; the evaluator logic itself is small and pure (fits the existing `HealthEvaluator` tests).

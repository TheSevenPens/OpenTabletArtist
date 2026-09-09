# Design — ship the official OTD release, and support the one already installed

> Status: **proposed.** Supersedes the daemon-acquisition half of
> [`136-bundling-binaries.md`](136-bundling-binaries.md) and reshapes
> [Phase 6](macos/implementation-plan.md) of the macOS port.

## Summary

Stop building our own OpenTabletDriver daemon. Acquire the **official OTD release binaries** instead,
and make **"drive the OTD that's already on this machine"** a first-class, supported mode rather than a
warning.

Today OTA compiles the daemon from the pinned submodule and ships that build
([`release.yml:61`](../../.github/workflows/release.yml:61)). The submodule is vanilla upstream at tag
`v0.6.7` — no local commits, no patches — so what we ship is *the same source*, rebuilt. This document
argues that "same source, our build" is the wrong unit to ship, and that the daemon should be an
artifact we **obtain**, not one we **produce**.

## Why

### 1. An official daemon is a supportable daemon

The OTD maintainers should never be asked to debug a binary they didn't produce. Even when the source is
byte-identical to their release tag, our build differs in ways that matter to triage: different compiler
invocation, different runtime bundling, a fresh assembly MVID on every CI run. From their side that is
effectively an unsupported fork, and a bug report against it is one they cannot reproduce or accept. This
has been discussed with them directly; the position is understood, not assumed.

Shipping their artifact converts every OTA bug report into a report against a version they recognise.

### 2. People want to use OTA against an OTD they already run

Users have told us they like OTA's setup experience but stay on OTD because OTD exposes configuration OTA
does not. Today that's an either/or: OTA treats an externally started daemon as a `daemon.foreign`
*Recommendation* and tells the user to "restart it to use the bundled build"
([`Health.cs:302`](../../OpenTabletArtist/Domain/Health/Health.cs:302)). That advice is backwards for
this group — it asks them to abandon the install they deliberately chose.

Making an existing OTD a supported target turns OTA into a **configuration utility for OTD**, usable
alongside OTD's own UX rather than instead of it. The groundwork is already there: OTA deliberately keeps
the shared `settings.json` valid for OTD's UI
([`AppSession.cs:749`](../../OpenTabletArtist/Services/AppSession.cs:749)), and the health model already
re-validates on a timer because "OTD's own UX can change settings underneath us"
([`317-remediation-model.md`](317-remediation-model.md)).

### 3. OTA as an accelerator for installing and configuring OTD

If OTA can install the official OTD release and perform first-time configuration, it becomes worth
running even for someone whose destination is OTD. Install, get a tablet working, set up an area and a
pressure curve, then keep using OTA or don't. That is a genuinely useful product on its own, and it costs
us almost nothing beyond the acquisition work this document already proposes.

### 4. OTA as a troubleshooting tool for OTD

OTA's health catalog, diagnostics, and remediation cards are the most developed part of the app and are
almost entirely about *OTD's* state — driver conflicts, Windows Ink plugin installed/enabled, VMulti,
mapping sanity, elevation. None of that needs an OTA-owned daemon to be useful. Pointed at someone
else's OTD install, OTA is a diagnostic front-end for a driver that otherwise has none.

### 5. It resolves the macOS permissions dead end

macOS ties Input Monitoring / Accessibility grants to a binary's identity. A daemon rebuilt on every CI
run gets a new identity each time, so the grant does not survive. This is the catch-22 the port kept
hitting and the reason the current macOS build tells the user to start OTD.app first
([`build-macos-app.sh`](../../scripts/build-macos-app.sh)).

A fixed, official artifact — installed once at a stable path — has a stable identity. The grant is asked
for once and then persists across OTA updates, because OTA updates no longer change the daemon.

### 6. Uniformity across platforms

Windows, macOS, and eventually Linux all converge on the same story: *find the official OTD, or install
it, then drive it*. The per-platform divergence today is not a design choice, it's an accident of what
each platform's build happened to make possible.

## Constraints (verified, not assumed)

These were checked against the pinned submodule and the OTD release installed on the development Mac.
They shape the design and must not be discovered again later.

| Constraint | Evidence | Consequence |
|---|---|---|
| OTD's **Windows** release is framework-dependent | `SELF_CONTAINED` defaults to `false` ([`lib.sh:109`](../../external/OpenTabletDriver/eng/bash/lib.sh:109)); the `win-*` branch sets only `SINGLE_FILE` ([`package.sh:72`](../../external/OpenTabletDriver/eng/bash/package.sh:72)) | Using it introduces a **.NET 8 runtime prerequisite** where OTA is currently self-contained. Already noted in [`136-bundling-binaries.md`](136-bundling-binaries.md). |
| OTD's **macOS** release is **x64-only** | publish matrix is `runtime-suffix: [x64]` (`build-matrix.yml`) | On Apple Silicon the daemon runs under **Rosetta 2**. OTA itself stays native arm64; they're separate processes over RPC, so mixed arch is fine. |
| OTD's **macOS** release is **unsigned** | `codesign -dvv` on the installed `OpenTabletDriver.Daemon` reports *"code object is not signed at all"*; `macos-signed` exists in the build wrapper but is **not** in the publish matrix | The Phase 6 premise that "OTD's *signed* daemon keeps its grant" ([`HANDOFF.md:59`](macos/HANDOFF.md:59)) is **wrong as stated**. Stability, not signature, is what makes the grant persist. |
| OTA compiles **against** OTD libraries | `ProjectReference` to `OpenTabletDriver.Desktop`/`.Plugin`/`.Configurations` ([`OpenTabletArtist.csproj:56`](../../OpenTabletArtist/OpenTabletArtist.csproj:56)); RPC deserializes into `Settings`, `AppInfo`, `LogMessage` ([`DaemonClient.cs:158`](../../OpenTabletArtist/Services/DaemonClient.cs:158)) | Version skew between OTA's linked types and a user's daemon becomes **possible**. The wire protocol is loosely typed JSON-RPC with stringly-named methods, so skew fails at **runtime**, not build time. A compatibility policy is now mandatory, not optional. |

Rosetta and the .NET prerequisite are real costs. They are accepted here because they are **exactly what
an OTD user already lives with** — the goal is parity with the official experience, and the current Mac
already runs OTD under Rosetta today.

## Daemon acquisition modes

Three modes, one resolution order. All three end at an official binary.

1. **Adopt** — an OTD install is already present and/or running. OTA connects and configures it. No
   installation, no mutation of the user's setup without consent.
2. **Bundled** — OTA ships the official release artifact inside its own package and launches it from a
   stable path. The zero-decision default for someone who has never heard of OTD.
3. **Assisted install** — no OTD anywhere. OTA offers to fetch and install the official release, then
   proceeds as *Adopt*.

Mode is **observable state, not a hidden preference**: the Daemon page must always answer "which OTD am I
talking to, where does it live, and who started it."

## Setup experience

### Detection

[`DaemonExePaths.Candidates`](../../OpenTabletArtist/Domain/DaemonExePaths.cs) grows from two cases
(bundled, dev tree) to an ordered ladder that includes real install locations — `/Applications/
OpenTabletDriver.app` on macOS, the standard install and portable layouts on Windows — plus a
**user-specified path** for people who keep OTD somewhere unusual. Detection stays a pure, ordered,
unit-testable function; only the candidate list changes.

Also detect a **running** daemon whose path we don't recognise. `FindExe` already falls back to a running
process ([`DaemonLifecycleService.cs:62`](../../OpenTabletArtist/Services/DaemonLifecycleService.cs:62));
that path becomes an adoption source rather than a curiosity.

### When OTD isn't found

This is the new first-run branch and the heart of the UX work. The user is asked one question with three
answers:

- **"Install it for me"** → assisted install of the official release.
- **"It's already on my system"** → a path picker, validated before acceptance.
- **"Use the copy that came with OTA"** → the bundled artifact (where we ship one).

The question must be answerable by someone who does not know what a daemon is. Frame it in terms of the
driver, not the process.

### Health checks

The catalog gains a small set of issues, following the existing severity tiers and
`HealthIssue`/`Remediation` shape:

| Id | Severity | Meaning |
|---|---|---|
| `otd.notFound` | Broken | No OTD anywhere. Remediation opens the install/locate flow. |
| `otd.notRunning` | Broken | Found but not running; remediation starts it. |
| `otd.versionUnsupported` | Broken | Daemon version outside the supported range. |
| `otd.versionUntested` | Recommendation | Newer than the pinned tag; probably fine, unverified. |
| `otd.permissionsMissing` | Broken | macOS: daemon lacks Input Monitoring / Accessibility. Deep-link to the right settings pane. |

`daemon.foreign` is **removed as a recommendation**. Connecting to someone else's OTD is no longer a
defect to fix; it becomes *Information* at most, stating which install is in use.

### Consent when configuring someone else's OTD

Adopting an existing install means writing to a configuration the user owns and may also be editing in
OTD's UX. Rules:

- **Plugin installs require explicit consent.** OTA currently installs the Dynamics and Windows Ink
  plugins into the daemon's plugin directory. Against an adopted install that is a mutation of the user's
  environment and must be asked for, with a plain statement of what changes.
- **Don't fight OTD's updater.** OTD has its own update path (`CheckForUpdates` over RPC). OTA neither
  suppresses nor duplicates it for adopted installs.
- **Keep `settings.json` OTD-valid**, as we already do.
- **Re-validate on a timer**, as the health model already does, because the other UX may be open.

### Feature gating

Per-app profile switching is currently disabled against a foreign daemon
([`PerAppViewModel.cs:49`](../../OpenTabletArtist/ViewModels/PerAppViewModel.cs:49)) because snapshot
paths and filter stores may not match. That gate needs revisiting: with adoption supported, "foreign" is
the normal case, and a feature that silently disables itself for most users is worse than one that states
its requirement. Either make it work against an adopted install or give it an explicit,
explained precondition — not a generic "external daemon" banner.

## Version compatibility policy

Required once a user's own OTD can be the target.

- OTA declares a **supported daemon range**, anchored to the submodule tag it compiles against.
- The daemon's version is already read on connect (`GetApplicationInfo`, plus
  `DaemonVersion.Read`) — surface it, compare it, and drive the two `otd.version*` issues above.
- CI asserts the submodule sits on an **exact release tag** (`git -C external/OpenTabletDriver describe
  --exact-match`), so "we build against a real release" is enforced rather than remembered.
  [`otd-release-watch.yml`](../../.github/workflows/otd-release-watch.yml) already tracks tags, not
  branch tips — this closes the loop.
- The packaged artifact's version and the pinned tag must match, checked at package time.

## Implementation plan

**macOS first — because there is no alternative there.** The permissions problem makes a self-built
daemon unworkable on macOS, so macOS is where this design is forced, and therefore where it gets proven.
Every piece of new UX (detection ladder, not-found flow, assisted install, permissions health check,
adoption consent) is built and validated on macOS before Windows is touched. Windows keeps its current
self-built daemon throughout, so nothing regresses while the model is being worked out.

### Phase A — macOS adoption (no bundling yet)
Detection ladder including `/Applications`, user-specified path, daemon version surfacing and the
`otd.*` health issues, `daemon.foreign` demotion, permissions check with a deep link. Outcome: OTA on
macOS is a supported front-end for an installed OTD.app. This alone replaces the "start OTD.app first"
instruction with something a user can follow.

### Phase B — macOS assisted install
Fetch and install the official OTD release when none is present, with the first-run question above.
Outcome: a clean Mac goes from nothing to a working tablet inside OTA.

### Phase C — macOS packaging
Decide bundled-vs-install-only for the `.app` in light of the x64/Rosetta constraint. Retire the
signing-based rationale in the Phase 6 plan and replace it with binary stability. This is where the old
Phase 6 lands, minus its incorrect premise.

### Phase D — Windows
Port the proven model. The open question is whether Windows ships **bundled** official binaries (adding a
.NET 8 prerequisite) or goes **install/adopt-only**. Decide with Phase A–C experience in hand, not now.

### Phase E — Linux
Follow-on. Distro packaging makes *adopt* the natural default and bundling largely pointless; see
[`192-linux-feasibility.md`](192-linux-feasibility.md).

## Open questions

- **Windows bundling.** Is a .NET 8 prerequisite acceptable in exchange for an official binary, or is
  install/adopt-only the better Windows story? Deliberately deferred to Phase D.
- **arm64 macOS.** Worth asking upstream whether an `osx-arm64` artifact is on the table; it would remove
  the Rosetta cost entirely. Cheap to ask, high payoff.
- **Signed macOS artifact.** `macos-signed` exists in OTD's build wrapper but isn't published. Also worth
  raising upstream.
- **Update ownership for bundled installs.** If OTA ships the artifact, who updates it — OTA's release
  cadence, or OTD's own updater?
- **Redistribution compliance.** Shipping OTD's binaries extends the redistributor obligations already
  recorded in [`136-bundling-binaries.md`](136-bundling-binaries.md); notices need review.

## What would revert this

- Upstream stating that a rebuilt-from-tag daemon is supportable after all, *and* adoption proving
  unpopular — removing both primary motivations.
- Version skew against user-installed OTD versions proving unmanageable in practice, making a pinned,
  known daemon the only reliable configuration.

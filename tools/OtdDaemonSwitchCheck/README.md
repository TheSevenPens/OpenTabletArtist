# OtdDaemonSwitchCheck

Checks what happens to a pending settings change when the OpenTabletDriver daemon underneath OTA is
replaced — against two **real** daemons, not fakes.

Users run more than one OpenTabletDriver build, in folders of their choosing, and switch between them
while OTA is open. Nearly everything the settings session holds is a fact about one daemon: the change it
accepted but never wrote, the file that change was for, what its settings file last held, whether it is
running a transient override. Carrying any of it across is how an edit ends up in the wrong install's
`settings.json`.

That behaviour was fixed five times (#774, #777, #787, #789, #803) and, until this tool, was only ever
verified against fakes. Everything the unit tests substitute is real here: the named pipe, the Win32
pipe-to-process-id lookup, the process path resolution, OTA's settings policy, and the disk.

## Running it

In `OpenTabletArtist.slnx`, so CI compiles it — but CI never runs it. Everything needing a real daemon is
behind command-line arguments, so a build is a compile check and nothing more.

That is a correction, not the original plan. This started outside the solution like `tools/OtdLinuxSetup`,
and within a day a change to `OtdSession`'s API broke it with nothing to say so. A tool that consumes an
evolving library API is worth a compile check, and this one consumes the API that is actively changing.

**The policy for `tools/`, since the two members now differ:**

| | built by CI | verified by |
|---|---|---|
| `OtdDaemonSwitchCheck` | yes, never run | the command above, by hand, against two real installs |
| `OtdLinuxSetup` | no | nothing automated |

`OtdLinuxSetup` is **intentionally unvalidated**, not merely unbuilt — that distinction matters, because
"excluded because nobody builds it" is circular reasoning. It is a reference implementation held for
[#589](https://github.com/TheSevenPens/OpenTabletArtist/issues/589), and its Avalonia 11.3.0 / net8.0 pin
is justified by nothing building it. Bringing it into the solution means revisiting that pin in the same
change, which is a decision about #589 rather than about build hygiene.

```bash
dotnet run --project tools/OtdDaemonSwitchCheck -- "C:\path\to\A\OpenTabletDriver.Daemon.exe" "C:\path\to\B\OpenTabletDriver.Daemon.exe"
```

Add `1`, `2`, `3` or `4` to run one scenario. Exit code is 0 when everything passed.

**Close OpenTabletArtist first.** The tool refuses to run otherwise, and the refusal is the point: a
running OTA connects to the same daemon, reloads, applies its filter policy and writes settings on every
load, so it would race this and make every result unattributable.

It starts and kills daemon processes and toggles the read-only flag on the settings file the daemon
reports. It restores that flag. It does **not** restore which daemon was running — start the one you want
again when you are done.

## What it checks

| | |
|---|---|
| 1 | A **different** daemon with an unsaved edit pending → change detected, edit discarded, nothing left to write into the new daemon's file |
| 2 | A different daemon with **nothing** pending → change detected, nothing discarded |
| 3 | The **same** daemon restarted with an edit pending → *not* a change, edit survives, retry lands it once the file is writable |
| 4 | A daemon whose executable **cannot be read** → not a change, edit survives |

Three of the four assert that a change was *not* reported, and that is deliberate. A build that discarded
state on every reconnect would pass scenario 1 and silently eat an artist's unsaved edit in ordinary use —
a worse bug than the one being guarded against, and one that looks like the app working.

Scenario 4 is the sharpest version of that: an elevated daemon, or another user's, is unreadable on
*every* reconnect, so treating "cannot see" as "it changed" would discard an edit every single time.

## Two things it does not establish

**Elevation is composed, not run.** Triggering UAC is out of scope, so scenario 4 combines two verified
facts: that `DaemonLifecycleService.PathOf` genuinely returns null for a process it cannot read (probed
against a SYSTEM-owned process), and that a real daemon over a real pipe behaves correctly when its id
cannot be resolved. What stays unproven is that an elevated daemon still yields a process id from
`GetNamedPipeServerProcessId`. Likely — the pipe is `CurrentUserOnly` and an elevated daemon runs as the
same user — but unobserved. Launching one elevated by hand closes it.

**Two installs may share one settings file.** An OpenTabletDriver install that has not been made portable
writes to `%LOCALAPPDATA%\OpenTabletDriver\settings.json` regardless of where the executable lives. When
both installs share it, the guard that refuses to retry a write whose destination has moved cannot fire,
and only the executable-identity check is under test. Run `convert_to_portable.bat` in one of them to
exercise both.

## Identity is the executable, not the process

A daemon stopped and started from the same path is the same daemon here, and correctly reports no change.
What the protected state describes is a settings file and an installation, and both survive a restart.
This is not a detector for every replacement process.

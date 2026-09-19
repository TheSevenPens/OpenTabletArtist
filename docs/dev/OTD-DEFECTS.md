# Defects found in OpenTabletDriver

## Status: working notes, not yet sent

This is a **draft for our own use**. Nothing here has been raised with the OpenTabletDriver team, and no
issue has been filed in any repository. The plan is to clarify each item, build a minimal reproduction
that does not involve OpenTabletArtist, and only then start a conversation.

Treat the "Not yet established" sections as the work remaining before any of this is worth sending.

**Companion document:** [`OTD-SUGGESTIONS.md`](OTD-SUGGESTIONS.md) collects *API gaps* — places where a
small daemon-side addition would let a UI do the right thing. Those are explicitly not blockers. **This
file is different in kind:** these are defects with user-visible consequences, one of which loses data.
Mixing them would misrepresent both.

**Pinned at:** submodule `v0.6.7`, commit `736003e`. Every line reference below is against that commit and
should be re-checked before sending, since they will drift.

---

## 1. `SaveSettings` dereferences areas that its own default constructor leaves null

### The code

`OpenTabletDriver.UX/MainForm.cs:549`, in `SaveSettings`:

```csharp
if (settings.Profiles.Any(p => p.AbsoluteModeSettings.Tablet.Width + p.AbsoluteModeSettings.Tablet.Height == 0))
```

`p.AbsoluteModeSettings.Tablet` is dereferenced with no null check. Two things can be null there —
`AbsoluteModeSettings` itself, and its `Tablet`.

### Why that shape exists

This is the part worth leading with, because it is not an exotic input.

`OpenTabletDriver.Desktop/Profiles/AbsoluteModeSettings.cs:9`:

```csharp
private AreaSettings display, tablet;      // no initialisers
```

`OpenTabletDriver.Desktop/Profiles/Profile.cs:16`:

```csharp
private AbsoluteModeSettings absoluteMode = new AbsoluteModeSettings();
```

So **`new Profile()` produces a profile whose `AbsoluteModeSettings.Tablet` and `.Display` are both
null** — which is exactly the shape the line above crashes on. The areas are only populated by
`AbsoluteModeSettings.GetDefaults(digitizer)` (`:47`), reached through `Profile.GetDefaults(tablet)`
(`Profile.cs:76`).

`GetDefaults` calls `AppInfo.PluginManager.GetService<IVirtualScreen>()`, a service present in the daemon
process and not in an arbitrary client. That is why a client cannot simply call it, and why a
client-constructed profile lands in the crashing shape by default rather than by mistake.

Deserialisation reaches it too: the properties use `RaiseAndSetIfChanged` with no null coalescing, so a
settings file containing `"AbsoluteModeSettings": null`, or one missing the `Tablet` key, produces the
same nulls.

### How it reaches the UX from another client

The obvious route is the settings file. It is **not the only one**, and this was the part we got wrong
in our own reasoning for a while.

`OpenTabletDriver.UX/MainForm.cs:489`:

```csharp
private static async Task SyncSettings()
{
    App.Current.Settings = await App.Driver.Instance.GetSettings();
}
```

called at `:425` and wired at `:426`:

```csharp
App.Driver.Resynchronize += async (sender, e) => await SyncSettings();
```

So the UX pulls settings **from the daemon** on every resync, not only at startup. A profile put on the
daemon by any client — never written to any file — becomes `App.Current.Settings`, and the next time the
user presses Save, the line above dereferences it.

### What OTA does instead

`OtdInterop/ProfileSanitizer.cs` fills null `AbsoluteModeSettings`, `Tablet` and `Display` before any
settings leave our library, on every path that sends. It only replaces nulls; existing areas are never
altered. See OTA issue #836 for why "only on the path that writes a file" was insufficient.

### Not yet established

- **What the UX actually does when it throws.** We have not run it. `SaveSettings` is invoked from
  `saveButton` (`:44`) and a menu command (`:261`), both as `async (s, e) => await SaveSettings()`, so the
  returned task is not observed. That may surface as an unhandled task exception, or may be swallowed.
  **If it is swallowed the outcome is arguably worse than a crash:** `DisableApplySaveButtons()` runs
  first (`:551`), so the user would be left with disabled Save and Apply buttons, nothing saved, and no
  error shown. This needs to be observed rather than reasoned about before we describe it to anyone.
- A minimal reproduction that does not involve OTA — most directly, a small client that calls
  `SetSettings` with a `new Profile()` and then a manual Save in the UX.
- Whether any OTD-internal path can produce the shape without a third-party client. `Profile.GetDefaults`
  is the normal route and populates the areas, so this may be reachable only from outside.

### Possible shapes of a fix

Not for us to decide, listed to make the conversation concrete:

- null-guard the predicate in `SaveSettings`
- give `AbsoluteModeSettings` non-null field initialisers, so the default constructor is safe
- validate on the daemon's `SetSettings` boundary, which would protect every client at once

---

## 2. `Settings.Serialize` deletes the file before writing, and reports success when it fails

### The code

`OpenTabletDriver.Desktop/Settings.cs:138`:

```csharp
public void Serialize(FileInfo file)
{
    try
    {
        if (file.Exists)
            file.Delete();

        using (var sw = file.CreateText())
        using (var jw = new JsonTextWriter(sw))
            serializer.Serialize(jw, this);
    }
    catch (UnauthorizedAccessException)
    {
        Log.Write("Settings", $"OpenTabletDriver doesn't have permission to save persistent settings to {file.DirectoryName}", LogLevel.Error);
    }
}
```

### Two separate defects in one method

**a. Destructive before safe.** The existing settings are deleted before the replacement is known to be
writable. An interruption between the two — a crash, a power loss, or the create failing — leaves **no
settings file at all**, rather than the previous one.

**b. Failure is logged, not reported.** `Serialize` returns `void` and swallows
`UnauthorizedAccessException`. A caller cannot distinguish "saved" from "your settings are gone".

The two combine into the bad case: on a directory that permits delete but denies create, the delete
succeeds, `CreateText` throws, the exception is logged, and the method **returns normally** — with the
user's settings destroyed and the caller told nothing. Other failures (`IOException`, disk full) are not
caught at all and propagate, which at least signals, though the file is gone either way.

### A second implementation, different behaviour

`OpenTabletDriver.Desktop/Serialization.cs:46` has the same delete-then-create with **no** catch:

```csharp
public static void Serialize(FileInfo file, object value)
{
    if (file.Exists)
        file.Delete();

    using (var fs = file.Create())
        Serialize(fs, value);
}
```

Used by `DesktopPluginManager.cs:201`. Same destructive ordering; it propagates rather than swallowing,
so it has (b) fixed and (a) not. Worth mentioning both together so a fix does not land on only one.

### What OTA does instead

`OtdInterop/AtomicFile.cs` writes the content to a temporary file in the same directory, flushes it to
disk, and only then swaps it in — keeping what it replaced as a `.bak`, which is by construction the last
content known to have been written successfully. A failure at any point leaves the previous file exactly
where it was. `OtdInterop/SettingsFileStore.cs` reports success or failure to its caller rather than
swallowing it, and falls back to the `.bak` when the main file is unreadable.

### Not yet established

- A minimal reproduction: a directory that permits delete and denies create is the clearest, but the
  exact ACL setup to demonstrate it on Windows needs writing down, and the Linux/macOS equivalent
  (a writable file in a non-writable directory) behaves differently and should be stated separately.
- Whether `Log.Write` at that point is visible to a user in practice, or only in the daemon log they
  would have no reason to open.
- Whether any caller of `Settings.Serialize` currently depends on its swallowing behaviour, which would
  make changing the signature more than a local fix.

### Possible shapes of a fix

- write-aside-and-swap, as we do — `File.Replace` handles the swap and the backup in one operation on
  the platforms that support it
- at minimum, create-then-replace rather than delete-then-create, which fixes (a) without touching the
  signature
- return a result, or let the exception propagate, so a caller can tell the user

---

## Before this goes anywhere

1. Re-check every line reference against whatever OTD commit is current at the time, not `736003e`.
2. Build the reproductions. Each defect should be demonstrable without OpenTabletArtist in the picture —
   otherwise the first question will be whether OTA is doing something unusual, and it will be a fair one.
3. Resolve item 1's "what does the UX actually do" question by observing it. The difference between
   "crashes" and "silently disables the buttons" changes both the severity and the description.
4. Decide what we are offering. We have working implementations of both workarounds and would be glad to
   contribute them, but the shape upstream wants may not be the shape we built for ourselves.

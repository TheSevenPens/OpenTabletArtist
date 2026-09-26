namespace OtdHealth.Collector;

/// <summary>Collects partial evidence without remediation. Calls are bounded even when a supplied
/// source ignores cancellation. Results arriving after a deadline are discarded.</summary>
public static class HealthCollector
{
    public static async Task<HealthAnalysisReport> CollectAsync(HealthSources source, HealthPolicy? policy = null,
        HealthCollectionOptions? options = null, CancellationToken cancellationToken = default)
    {
        policy ??= new(); options ??= new();
        if (options.ProbeTimeout <= TimeSpan.Zero || options.TotalTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Timeouts must be positive.");
        var coverage = options.Coverage.Distinct().ToArray();
        if (coverage.Any(id => !Enum.IsDefined(id))) throw new ArgumentException("Unknown probe.", nameof(options));
        // All daemon-dependent observations require connection evidence. Narrow coverage cannot bypass it.
        if (coverage.Any(IsDaemonDependent) && !coverage.Contains(ProbeId.Daemon))
            throw new ArgumentException("Daemon-dependent coverage requires the Daemon probe.", nameof(options));
        if (coverage.Any(id => id is ProbeId.Displays or ProbeId.ConfigurationOverrides or ProbeId.MacOSAccess)
            && !coverage.Contains(ProbeId.Profiles))
            throw new ArgumentException("Mapping, overrides and macOS access require Profiles coverage.", nameof(options));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.TotalTimeout);
        var results = new List<ProbeResult>();
        var connected = false;
        async Task<T?> Run<T>(ProbeId id, Func<CancellationToken, Task<T>> read, bool applicable = true)
        {
            if (!coverage.Contains(id)) return default;
            if (!applicable)
            {
                results.Add(new(id, true, false, ProbeOutcome.NotApplicable)); return default;
            }
            ProbeOutcome outcome; ProbeFailure? failure = null;
            try
            {
                deadline.Token.ThrowIfCancellationRequested();
                if (IsDaemonDependent(id) && !connected)
                    throw new ProbeUnavailableException("The daemon is not connected.");
                using var probe = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                probe.CancelAfter(options.ProbeTimeout);
                T value;
                try
                {
                    var token = probe.Token;
                    value = await Task.Run(() => read(token), token).WaitAsync(token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException) when (!deadline.IsCancellationRequested)
                { throw new TimeoutException("The probe exceeded its time limit."); }
                deadline.Token.ThrowIfCancellationRequested();
                results.Add(new(id, true, true, ProbeOutcome.Completed));
                return value;
            }
            catch (ProbeUnavailableException ex)
            {
                outcome = ex.Unsupported ? ProbeOutcome.Unsupported : ProbeOutcome.Unavailable;
                failure = new(outcome.ToString(), ex.Message);
            }
            catch (OperationCanceledException)
            {
                outcome = cancellationToken.IsCancellationRequested ? ProbeOutcome.Cancelled : ProbeOutcome.Failed;
                failure = new(cancellationToken.IsCancellationRequested ? "Cancelled" : "DeadlineExceeded", "Collection was interrupted.");
            }
            catch (Exception ex)
            {
                outcome = ProbeOutcome.Failed;
                failure = new(ex is TimeoutException ? "Timeout" : "ReadFailed", ex.Message, ex.GetType().FullName);
            }
            results.Add(new(id, true, true, outcome, failure));
            return default;
        }

        connected = await Run(ProbeId.Daemon, async ct =>
        {
            if (!await source.Connect(ct).ConfigureAwait(false)) throw new ProbeUnavailableException("The daemon is disconnected.");
            return true;
        });
        var version = await Run(ProbeId.DaemonVersion, async ct =>
        {
            var value = await source.Version(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(value)) throw new ProbeUnavailableException("The daemon version is unknown.");
            return value;
        });
        var profiles = await Run(ProbeId.Profiles, async ct =>
        {
            var value = await source.Profiles(ct).ConfigureAwait(false);
            ProfileInspector.Validate(value);
            return value;
        });
        bool hasDetected = profiles?.Any(p => p.Detected) == true;
        bool profilesKnown = profiles != null;
        var displays = await Run(ProbeId.Displays, async ct =>
        {
            if (!profilesKnown) throw new ProbeUnavailableException("Profiles could not be collected.");
            var value = await source.Displays(ct).ConfigureAwait(false);
            if (value.Count == 0) throw new ProbeUnavailableException("No display layout is available.");
            if (value.Any(d => d == null || !float.IsFinite(d.X) || !float.IsFinite(d.Y)
                || !float.IsFinite(d.Width) || !float.IsFinite(d.Height) || d.Width <= 0 || d.Height <= 0))
                throw new InvalidDataException("The display source returned invalid bounds.");
            return value;
        }, !profilesKnown || hasDetected);
        var overrides = await Run(ProbeId.ConfigurationOverrides, async ct =>
            FileProbes.ConfigurationOverrides(await source.ConfigurationDirectory(ct).ConfigureAwait(false)), !profilesKnown || hasDetected);
        var ink = await Run(ProbeId.WindowsInk, async ct =>
            FileProbes.WindowsInk(await source.PluginDirectory(ct).ConfigureAwait(false), policy.ExpectedOtdVersion), source.Platform == HealthPlatform.Windows);
        // Box scalar observations so failed reads remain unknown instead of becoming false.
        var vmulti = await Run<bool?>(ProbeId.VMulti, async ct => await source.VMulti(ct).ConfigureAwait(false), source.Platform == HealthPlatform.Windows);
        var conflicts = await Run(ProbeId.DriverConflicts, source.Conflicts);
        var elevated = await Run(ProbeId.ProcessElevation, source.ProcessElevation, source.Platform == HealthPlatform.Windows);
        bool linuxApplicable = source.Platform == HealthPlatform.Linux && !hasDetected;
        var udev = await Run<bool?>(ProbeId.LinuxUdev, async ct => await source.LinuxUdev(ct).ConfigureAwait(false), linuxApplicable);
        var modules = await Run(ProbeId.LinuxModules, source.LinuxModules, linuxApplicable);
        var access = await Run(ProbeId.LinuxHidAccess, source.LinuxHidAccess, linuxApplicable);
        var mac = await Run(ProbeId.MacOSAccess, ct =>
        {
            if (!profilesKnown) throw new ProbeUnavailableException("Profiles could not be collected.");
            return source.MacOSAccess(ct);
        }, source.Platform == HealthPlatform.MacOS && !hasDetected);
        var snapshot = new HealthSnapshot
        {
            Platform = source.Platform,
            DaemonConnected = connected,
            DaemonVersion = version ?? "",
            ExpectedOtdVersion = policy.ExpectedOtdVersion,
            ForeignDaemon = policy.ForeignDaemon,
            DaemonIsManagedButNotSelected = policy.DaemonIsManagedButNotSelected,
            DaemonSourceUnknown = policy.DaemonSourceUnknown,
            DaemonCannotOpenTablet = mac,
            WinInkInstalled = ink?.Installed,
            WinInkVersionMismatch = ink?.VersionMismatch == true,
            VMultiInstalled = vmulti,
            HasDriverConflict = conflicts?.HasConflict == true,
            BlockingDriverConflict = conflicts?.Blocking == true,
            RunningElevated = elevated,
            LinuxUdevRulesMissing = udev == false,
            LinuxConflictingModulesLoaded = modules?.Loaded ?? [],
            LinuxConflictingModulesNotBlacklisted = modules?.NotBlacklisted == true,
            LinuxHidAccess = access?.Access ?? HidAccessStatus.NoProblemReported,
            LinuxUserManagerRunning = access?.UserManagerRunning == true,
            Tablets = (profiles ?? []).Select(p => ProfileInspector.Read(p, displays ?? [], overrides ?? new HashSet<string>(), policy)).ToArray(),
        };
        return new(snapshot, coverage, results.OrderBy(p => p.Id).ToArray());
    }

    internal static bool IsDaemonDependent(ProbeId id) => id is ProbeId.DaemonVersion or ProbeId.Profiles
        or ProbeId.ConfigurationOverrides or ProbeId.WindowsInk or ProbeId.DriverConflicts or ProbeId.MacOSAccess;
}

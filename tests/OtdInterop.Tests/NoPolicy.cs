using OpenTabletDriver.Desktop;
using OtdInterop;

namespace OtdInterop.Tests;

/// <summary>
/// A settings policy that does nothing.
/// </summary>
///
/// <remarks>
/// <para>
/// These tests used to pass <c>OtaSettingsPolicy</c>, the application's own rules, because it was the
/// implementation nearest to hand. None of them depended on what it did — they needed <em>a</em> policy,
/// and a change to OTA's filter rules could have failed a test about reconnect ordering.
/// </para>
/// <para>
/// Tests that are genuinely about the policy hook supply their own, which says something by contrast:
/// <c>HoardingPolicy</c> and <c>RenamingPolicy</c> exist precisely because a policy that does nothing
/// cannot show that the hook is called, or called once, or called on a copy.
/// </para>
/// </remarks>
internal sealed class NoPolicy : IOtdSettingsPolicy
{
    /// <summary>The shared instance. There is nothing to configure about doing nothing.</summary>
    public static readonly NoPolicy Instance = new();

    private NoPolicy() { }

    /// <inheritdoc />
    public void Apply(Settings workingCopy, SettingsPolicyContext context) { }
}

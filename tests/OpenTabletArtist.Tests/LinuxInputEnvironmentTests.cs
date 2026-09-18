using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// The parsing behind the Linux tablet prerequisites (#779), folded in from <c>tools/OtdLinuxSetup</c>.
///
/// Only the pure parsers are covered, deliberately: they take the file content as a string, so they run on
/// every CI lane rather than only the Linux one. The surrounding probe is filesystem and process access,
/// which is not meaningfully testable without the machine it describes.
///
/// The old tool matched these with <c>Contains</c>, which is where the bugs below came from.
/// </summary>
public class LinuxInputEnvironmentTests
{
    private static readonly string[] Conflicting = ["wacom", "hid_uclogic"];

    // --- /etc/modprobe.d ------------------------------------------------------------------

    [Fact]
    public void Blacklist_RecognisesBothModules()
    {
        Assert.True(LinuxInputEnvironment.AllModulesBlacklisted(
            "blacklist wacom\nblacklist hid_uclogic\n", Conflicting));
    }

    [Fact]
    public void Blacklist_NeedsEveryModule_NotJustOne()
    {
        Assert.False(LinuxInputEnvironment.AllModulesBlacklisted("blacklist wacom\n", Conflicting));
    }

    [Fact]
    public void Blacklist_ToleratesIndentationAndOtherEntries()
    {
        Assert.True(LinuxInputEnvironment.AllModulesBlacklisted(
            "# OpenTabletDriver\n  blacklist wacom  \nblacklist something_else\n\tblacklist hid_uclogic\n",
            Conflicting));
    }

    [Fact]
    public void Blacklist_IgnoresCommentedOutLines()
    {
        // A commented-out blacklist is the state you are in right after someone disabled it to test
        // something. Reading it as active would report a machine as configured when it is not.
        Assert.False(LinuxInputEnvironment.AllModulesBlacklisted(
            "blacklist wacom\n#blacklist hid_uclogic\n", Conflicting));
    }

    [Fact]
    public void Blacklist_DoesNotMatchALongerModuleName()
    {
        // Substring matching counted "blacklist wacom_w8001" as blacklisting "wacom".
        Assert.False(LinuxInputEnvironment.AllModulesBlacklisted(
            "blacklist wacom_w8001\nblacklist hid_uclogic\n", Conflicting));
    }

    // --- /proc/modules --------------------------------------------------------------------

    [Fact]
    public void LoadedModules_ReadsTheFirstFieldOfEachLine()
    {
        const string procModules =
            "hid_uclogic 45056 0 - Live 0x0000000000000000\n" +
            "usbhid 65536 0 - Live 0x0000000000000000\n";

        Assert.Equal(["hid_uclogic"], LinuxInputEnvironment.LoadedFrom(procModules, Conflicting));
    }

    [Fact]
    public void LoadedModules_ReportsNothingWhenNoneAreLoaded()
    {
        Assert.Empty(LinuxInputEnvironment.LoadedFrom(
            "usbhid 65536 0 - Live 0x0000000000000000\n", Conflicting));
    }

    [Fact]
    public void LoadedModules_DoesNotMatchAModuleThatMerelyDependsOnIt()
    {
        // "wacom" appears on this line as a dependency of wacom_w8001, not as a loaded module of its own.
        // The old substring check reported it as loaded and sent the user to rmmod something absent.
        const string procModules = "wacom_w8001 20480 0 - Live 0x0000000000000000\n";

        Assert.Empty(LinuxInputEnvironment.LoadedFrom(procModules, Conflicting));
    }

    [Fact]
    public void LoadedModules_DoesNotMatchAModuleListedOnlyAsADependency()
    {
        const string procModules =
            "hid_generic 16384 0 - Live 0x0000000000000000\n" +
            "usbhid 65536 1 wacom, Live 0x0000000000000000\n";

        Assert.Empty(LinuxInputEnvironment.LoadedFrom(procModules, Conflicting));
    }

    // --- /etc/group -----------------------------------------------------------------------

    [Fact]
    public void Group_FindsAMemberAmongSeveral()
    {
        const string etcGroup = "root:x:0:\ninput:x:104:alice,bob\nvideo:x:44:bob\n";

        Assert.True(LinuxInputEnvironment.GroupContains(etcGroup, "input", "bob"));
    }

    [Fact]
    public void Group_IsFalseWhenTheGroupExistsButTheUserIsNotIn()
    {
        Assert.False(LinuxInputEnvironment.GroupContains("input:x:104:alice\n", "input", "bob"));
    }

    [Fact]
    public void Group_IsFalseWhenTheGroupIsAbsentEntirely()
    {
        Assert.False(LinuxInputEnvironment.GroupContains("root:x:0:\n", "input", "bob"));
    }

    [Fact]
    public void Group_DoesNotMatchAUserWhoseNameMerelyContainsTheMembers()
    {
        // "bob" must not match "bobby" — the membership list is comma-separated, not free text.
        Assert.False(LinuxInputEnvironment.GroupContains("input:x:104:bobby\n", "input", "bob"));
    }

    [Fact]
    public void Group_HandlesCarriageReturnsAndAnEmptyMemberList()
    {
        Assert.False(LinuxInputEnvironment.GroupContains("input:x:104:\r\n", "input", "bob"));
        Assert.True(LinuxInputEnvironment.GroupContains("input:x:104:bob\r\n", "input", "bob"));
    }
}

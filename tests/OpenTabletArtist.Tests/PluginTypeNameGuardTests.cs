using System;
using System.IO;
using System.Linq;
using System.Reflection;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// Guards against drift between <see cref="PressureCurveProfile.FilterTypeName"/> (a hardcoded
/// string, since the app doesn't reference the plugin assembly) and the plugin's actual filter type.
/// If the plugin is renamed without updating the constant, the app would silently write a
/// PluginSettingStore the daemon can't construct — this test fails loudly instead (#104).
/// </summary>
public class PluginTypeNameGuardTests
{
    [Fact]
    public void FilterTypeName_ResolvesToABuiltPluginPipelineElement()
    {
        var dll = FindPluginDll();
        Assert.True(dll != null, "Pen-dynamics plugin DLL not found — build the solution (it compiles the plugin).");

        var asm = Assembly.LoadFrom(dll!);
        var type = asm.GetType(PressureCurveProfile.FilterTypeName);
        Assert.True(type != null,
            $"PressureCurveProfile.FilterTypeName ('{PressureCurveProfile.FilterTypeName}') matches no type in the " +
            "plugin assembly — the plugin was renamed; update the constant.");
        Assert.Contains(type!.GetInterfaces(), i => i.Name.StartsWith("IPositionedPipelineElement"));
    }

    [Fact]
    public void CalibrationFilterTypeName_ResolvesToABuiltPluginPipelineElement()
    {
        var dll = FindPluginDll();
        Assert.True(dll != null, "Plugin DLL not found — build the solution (it compiles the plugin).");

        var asm = Assembly.LoadFrom(dll!);
        var type = asm.GetType(CalibrationProfile.FilterTypeName);
        Assert.True(type != null,
            $"CalibrationProfile.FilterTypeName ('{CalibrationProfile.FilterTypeName}') matches no type in the " +
            "plugin assembly — the calibration filter was renamed; update the constant (#127).");
        Assert.Contains(type!.GetInterfaces(), i => i.Name.StartsWith("IPositionedPipelineElement"));
    }

    [Fact]
    public void HoverFilterTypeName_ResolvesToABuiltPluginPipelineElement()
    {
        var dll = FindPluginDll();
        Assert.True(dll != null, "Plugin DLL not found — build the solution (it compiles the plugin).");

        var asm = Assembly.LoadFrom(dll!);
        var type = asm.GetType(HoverProfile.FilterTypeName);
        Assert.True(type != null,
            $"HoverProfile.FilterTypeName ('{HoverProfile.FilterTypeName}') matches no type in the " +
            "plugin assembly — the hover filter was renamed; update the constant (#188).");
        Assert.Contains(type!.GetInterfaces(), i => i.Name.StartsWith("IPositionedPipelineElement"));
    }

    private const string PluginDll = "OpenTabletArtist.Dynamics.dll";

    // Walk up from the test output dir and locate the built plugin DLL (the test project doesn't
    // reference the plugin; the solution build produces it).
    //
    // Two layouts, because the output path is not always the repo's (#738): the default per-project
    // bin, and a shared output root — which is how the suite is run when the app is holding a lock on
    // the normal bin directory. Probing only the first made these tests fail for a reason that had
    // nothing to do with what they check.
    private static string? FindPluginDll()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            // Shared output root: the plugin's net8.0 folder sits beside the test binary's net10.0 one.
            var sibling = Path.Combine(dir.FullName, "net8.0", PluginDll);
            if (File.Exists(sibling)) return sibling;

            foreach (var cfg in new[] { "Debug", "Release" })
            {
                var candidate = Path.Combine(dir.FullName, "plugins", "OpenTabletArtist.Dynamics",
                    "bin", cfg, "net8.0", PluginDll);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }
}

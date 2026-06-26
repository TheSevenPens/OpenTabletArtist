using System;
using System.IO;
using System.Linq;
using System.Reflection;
using OtdWindowsHelper.Domain;
using OtdWindowsHelper.Services;
using Xunit;

namespace OtdWindowsHelper.Tests;

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
            "plugin assembly — the plugin was renamed; update the constant (and consider LegacyFilterTypeName).");
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

    // Walk up from the test output dir to the repo root and locate the built plugin DLL (the test
    // project doesn't reference the plugin; the solution build produces it).
    private static string? FindPluginDll()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            foreach (var cfg in new[] { "Debug", "Release" })
            {
                var candidate = Path.Combine(dir.FullName, "plugins", "OtdWindowsHelper.Dynamics",
                    "bin", cfg, "net8.0", "OtdWindowsHelper.Dynamics.dll");
                if (File.Exists(candidate)) return candidate;
            }
        return null;
    }
}

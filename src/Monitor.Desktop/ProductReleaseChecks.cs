// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Reflection;
using System.Runtime.Loader;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Monitor.Desktop;

// Invoked only by the development executable, inspecting the actual product DLL.
internal static class ProductReleaseChecks
{
    internal static string AssemblyPath { get; set; } = "";
    internal static void Verify()
    {
        var context = new AssemblyLoadContext("product-contract", isCollectible: true);
        context.Resolving += (_, name) => AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name.Name);
        var assembly = context.LoadFromAssemblyPath(AssemblyPath);
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        Require(assembly.GetType("Monitor.Desktop.DesignPreviewSmokeChecks") is null && assembly.GetType("Monitor.Desktop.ProductReleaseChecks") is null, "test runners excluded");
        var identity = assembly.GetType("Monitor.Desktop.ProductIdentity")!;
        Require(identity.GetProperty("DevelopmentFeatures", flags)!.GetValue(null) is false, "product mode");
        var entry = assembly.GetType("Monitor.Desktop.Program")!.GetMethod("Main", flags)!;
        foreach (string argument in new[] { "--ui-preview", "--study-demo", "--waveform-demo", "--physiology-demo", "--electrode-demo", "--smoke-test", "--smoke-shard", "--product-check", "--generate-style-previews" })
        { Require(entry.Invoke(null, [new[] { argument }]) is 2, "reject " + argument); }
        var create = assembly.GetType("Monitor.Desktop.MonitorApp")!.GetMethod("CreateLaunchWindow", flags)!;
        var window = (Window)create.Invoke(null, [Array.Empty<string>(), false])!;
        try
        {
            window.Show();
            Require(window.Title?.Contains("开发", StringComparison.Ordinal) == false, "clean title");
            object settings = window.GetType().GetProperty("Settings", flags)!.GetValue(window)!;
            var tabs = (ListBox)settings.GetType().GetProperty("Tabs", flags)!.GetValue(settings)!;
            var select = window.GetType().GetMethod("SelectPage", flags)!;
            select.Invoke(window, [2]); tabs.SelectedIndex = 6;
            Capture(window, "product-release-settings.png");
            var groups = (System.Collections.IDictionary)settings.GetType().GetProperty("SectionPages", flags)!.GetValue(settings)!;
            object advanced = groups[6]!;
            var advancedSections = (ListBox)advanced.GetType().GetProperty("Sections", flags)!.GetValue(advanced)!;
            string[] expectedAdvancedKeys = ["generation.ecg", "generation.respiration", "advanced.sectionApplied", "skin.defibrillator", "pressureRange.title"];
            string[] advancedKeys = (string[])advanced.GetType().GetField("_titles", flags)!.GetValue(advanced)!;
            Require(advancedKeys.SequenceEqual(expectedAdvancedKeys), "product advanced section keys");
            Require(advancedSections.Items.OfType<ListBoxItem>().Count(item => item.IsEnabled) == expectedAdvancedKeys.Length,
                "all product advanced sections enabled");
            var advancedTools = (Control)settings.GetType().GetField("_advancedTools", flags)!.GetValue(settings)!;
            Require(advancedTools.Parent is null, "advanced development tools unattached");
            tabs.SelectedIndex = 3;
            Capture(window, "product-release-sound.png");
            object sound = settings.GetType().GetProperty("Sound", flags)!.GetValue(settings)!;
            foreach (string name in new[] { "Audition", "Stop", "Status" })
            {
                var control = (Control)sound.GetType().GetProperty(name, flags)!.GetValue(sound)!;
                Require(control.Parent is null, "sound development control unattached: " + name);
            }
            foreach (string name in new[] { "OutputDevice", "Volume", "Mute" })
            {
                var control = (Control)sound.GetType().GetProperty(name, flags)!.GetValue(sound)!;
                Require(control.IsEffectivelyVisible, "sound output control visible: " + name);
            }
            object alerts = settings.GetType().GetProperty("Alerts", flags)!.GetValue(settings)!;
            var testLevel = (ComboBox)alerts.GetType().GetProperty("TestLevel", flags)!.GetValue(alerts)!;
            Require(testLevel.Parent is null, "alarm test controls unattached");
            tabs.SelectedIndex = 4;
            Capture(window, "product-release-alarms.png");
            Require(!window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("联调", StringComparison.Ordinal) == true), "no alarm test entry");
            select.Invoke(window, [4]); Capture(window, "product-release-about.png");
            Require(!window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("开发版", StringComparison.Ordinal) == true), "clean About");
            select.Invoke(window, [3]); Capture(window, "product-release-help.png");
            Require(!window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "四级报警联调"), "no development help topic");
            Require(!window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("试听三声", StringComparison.Ordinal) == true), "no sound audition help");
            Require(File.Exists(Path.Combine(Path.GetDirectoryName(AssemblyPath)!, "style-previews.bin")), "prebuilt preview catalog");
        }
        finally { window.Close(); context.Unload(); }
        Console.WriteLine("PASS actual product DLL: release identity, UI, assets and rejected development CLI");
    }
    private static void Capture(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        var root = (Control)window.Content!;
        root.Measure(new Size(window.Width, window.Height)); root.Arrange(new Rect(0, 0, window.Width, window.Height));
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height), new Vector(96, 96));
        bitmap.Render(root); Directory.CreateDirectory("artifacts"); bitmap.Save(Path.Combine("artifacts", name), PngBitmapEncoderOptions.Default);
    }
    private static void Require(bool condition, string message)
    { if (!condition) { throw new InvalidOperationException("Product release: " + message); } }
}

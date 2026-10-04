// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Monitor.Application.Presentation;
using Monitor.Infrastructure.Preferences;

namespace Monitor.Desktop;

internal static class DefaultResetSmokeChecks
{
    internal static void Verify()
    {
        string directory = Path.Combine(Path.GetTempPath(), "monitor-reset-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json");
        var window = new DesignPreviewWindow(path);
        window.Show();
        try
        {
            window.Settings.Skin.SelectedIndex = 0;
            window.Settings.PaperLayout.SelectedIndex = 1;
            window.Settings.Alerts.WarningHeartRate.Value = 140;
            window.RestartSettings();
            var previous = window.Session;
            window.Settings.RateSeed.Text = "invalid draft";
            window.SelectPage(2);
            window.Settings.ResetAll.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Require(ReferenceEquals(previous, window.Session) && window.Settings.RateSeed.Text == "invalid draft" && window.ResetConfirmation is not null,
                "reset first explains destructive consequences without changing the session or drafts");
            var cancel = window.ResetConfirmation!.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "取消");
            cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Require(window.ResetConfirmation is null && ReferenceEquals(previous, window.Session), "cancel preserves settings and history");
            window.Settings.ResetAll.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var confirm = window.ResetConfirmation!.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "恢复默认设置");
            Require(!confirm.IsDefault, "destructive confirmation is not activated by an accidental Return");
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Require(window.Settings.Sound.AlarmEnabled.IsChecked == false, "reset leaves monitoring audio disabled");
            var saved = new DisplayPreferenceStore(path).Load(out bool rejected);
            Require(!rejected && saved.Display.Skin == MonitorSkin.FiveRows && saved.PaperLayout == 0, "reset persists default display");
            Require(Equal(saved.Alarms, MonitorAlarmPreferences.Default) && Equal(saved.Sound, MonitorSoundPreferences.Default), "reset clears alarm and sound overrides");
            Require(!ReferenceEquals(previous, window.Session) && window.Settings.RateSeed.Text != "invalid draft", "reset replaces session and invalid drafts");
            var restarted = new DesignPreviewWindow(path);
            try { Require(Equal(restarted.Settings.CaptureGenerator(), window.Settings.CaptureGenerator()), "restart restores default generator"); }
            finally { restarted.Close(); }
            var unwritable = new DesignPreviewWindow(directory);
            try
            {
                unwritable.ResetAllSettings();
                Require(unwritable.PreferenceNotice.IsVisible && unwritable.Settings.Status.Text!.Contains("保存失败", StringComparison.Ordinal), "failed reset persistence remains visible");
            }
            finally { unwritable.Close(); }
            window.SelectPage(4);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var root = (Control)window.Content!;
            root.Measure(new Avalonia.Size(window.Width, window.Height));
            root.Arrange(new Avalonia.Rect(0, 0, window.Width, window.Height));
            var repository = window.GetVisualDescendants().OfType<HyperlinkButton>().Single();
            Require(repository.NavigateUri?.AbsoluteUri == ProductIdentity.RepositoryUrl && AutomationProperties.GetName(repository) == "项目 GitHub 仓库", "repository link is explicit and accessible");
            Require(ProductIdentity.CopyrightNotice.Contains("Copyright (C) 2026  Jason Zou", StringComparison.Ordinal) && ProductIdentity.CopyrightNotice.Contains("any later version", StringComparison.Ordinal), "copyright includes owner and authorized license grant");
        }
        finally { window.Close(); if (Directory.Exists(directory)) { Directory.Delete(directory, true); } }
    }
    private static bool Equal<T>(T left, T right) => System.Text.Json.JsonSerializer.Serialize(left) == System.Text.Json.JsonSerializer.Serialize(right);
    private static void Require(bool condition, string message)
    { if (!condition) { throw new InvalidOperationException(message); } }
}

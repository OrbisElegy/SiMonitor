// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Rendering;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Monitor.Desktop;

internal static class DeviceRotaryDialSmokeChecks
{
    private static readonly double[] Positions = [-140, -70, 0, 70, 140];

    internal static void Verify()
    {
        var localization = new DesktopLocalization("en");
        MonitorSkinMaterial material = MonitorSkinPackage.DefibrillatorEnclosure.Material("selector");
        var navigation = new DeviceRotaryDial(localization, material, "devicePanel.navigation", true);
        var selector = new DeviceRotaryDial(localization, material, "devicePanel.modeSelector", false);
        selector.ConfigurePositions(Positions, 2);
        var canvas = new Canvas { Width = 300, Height = 150 };
        Canvas.SetLeft(navigation, 15);
        Canvas.SetTop(navigation, 20);
        Canvas.SetLeft(selector, 165);
        Canvas.SetTop(selector, 20);
        canvas.Children.Add(navigation);
        canvas.Children.Add(selector);
        var window = new Window { Width = 300, Height = 150, Content = canvas };
        window.Show();
        try
        {
            DesktopViewportSmokeChecks.Layout(window, 300, 150);
            Require(!navigation.GetVisualDescendants().OfType<Button>().Any() && navigation.FocusAdorner is null,
                "the rotary surface has no rectangular button or focus template");
            Require(((ICustomHitTest)navigation).HitTest(Center(navigation)) &&
                !((ICustomHitTest)navigation).HitTest(new Point(1, 1)),
                "rotary hit testing includes the circle and excludes its rectangular corners");
            VerifyNavigation(window, navigation, selector);
            VerifySelector(window, selector);
            VerifyScaledHitTesting(window, canvas, navigation);
        }
        finally { window.Close(); }
    }

    private static void VerifyScaledHitTesting(Window window, Canvas canvas, DeviceRotaryDial navigation)
    {
        window.Content = null;
        window.Content = new Viewbox
        {
            Child = canvas,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        foreach (double scale in new[] { .6, 2.0 })
        {
            DesktopViewportSmokeChecks.Layout(window, 300 * scale, 150 * scale);
            window.UpdateLayout();
            WaitForHitTest(window, navigation);
            Point center = Position(window, navigation, Center(navigation));
            Point ring = Position(window, navigation, Ring(navigation, 0));
            Point corner = Position(window, navigation, new Point(1, 1));
            var centerHit = window.InputHitTest(center);
            var ringHit = window.InputHitTest(ring);
            var cornerHit = window.InputHitTest(corner);
            Require(ReferenceEquals(centerHit, navigation) && ReferenceEquals(ringHit, navigation) && !ReferenceEquals(cornerHit, navigation),
                $"native hit testing follows the round surface through Viewbox scaling {scale}; " +
                $"center {center} = {centerHit}, ring {ring} = {ringHit}, corner {corner} = {cornerHit}, window {window.Bounds}");
            int presses = 0;
            void Confirm() => presses++;
            navigation.Pressed += Confirm;
            var mouse = new Pointer(184, PointerType.Mouse, true);
            Press(window, navigation, mouse, Center(navigation));
            Release(window, navigation, mouse, Center(navigation));
            navigation.Pressed -= Confirm;
            Require(presses == 1 && mouse.Captured is null, "a scaled center click retains correct pointer coordinates and capture");
        }
    }

    private static void WaitForHitTest(Window window, DeviceRotaryDial dial)
    {
        // Layout completes on the UI thread, but native hit testing consumes
        // compositor readback published only after the render thread presents.
        var elapsed = Stopwatch.StartNew();
        var frame = new DispatcherFrame();
        var poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        poll.Tick += (_, _) =>
        {
            if (ReferenceEquals(window.InputHitTest(Position(window, dial, Center(dial))), dial) || elapsed.Elapsed > TimeSpan.FromSeconds(3))
            { frame.Continue = false; }
        };
        poll.Start();
        try { Dispatcher.UIThread.PushFrame(frame); }
        finally { poll.Stop(); }
    }

    private static void VerifyNavigation(Window window, DeviceRotaryDial navigation, DeviceRotaryDial selector)
    {
        int presses = 0;
        var rotations = new List<int>();
        navigation.Pressed += () => presses++;
        navigation.Rotated += rotations.Add;
        var mouse = new Pointer(181, PointerType.Mouse, true);
        Point center = Center(navigation);
        Press(window, navigation, mouse, center);
        Require(ReferenceEquals(mouse.Captured, navigation), "a navigation press captures its pointer");
        Release(window, navigation, mouse, center);
        Require(presses == 1 && mouse.Captured is null, "a center short press confirms once and releases capture");

        Press(window, navigation, mouse, center);
        Move(window, navigation, mouse, Ring(navigation, 0));
        Move(window, navigation, mouse, Ring(navigation, 90));
        Release(window, navigation, mouse, center);
        Require(rotations.Count >= 3 && rotations.All(direction => direction == 1) && presses == 1,
            "a center-to-ring drag rotates clockwise without confirming on release");
        rotations.Clear();
        var touch = new Pointer(182, PointerType.Touch, true);
        Press(window, navigation, touch, Ring(navigation, 90));
        Move(window, navigation, touch, Ring(navigation, 30));
        Release(window, navigation, touch, Ring(navigation, 30));
        Require(rotations.Count == 2 && rotations.All(direction => direction == -1) && presses == 1 && touch.Captured is null,
            "touch dragging rotates anticlockwise without relying on mouse button state");

        Press(window, navigation, mouse, center);
        mouse.Capture(null);
        Release(window, navigation, mouse, center);
        Require(presses == 1, "losing capture cancels a pending center press");
        Press(window, navigation, mouse, center);
        navigation.IsEnabled = false;
        Release(window, navigation, mouse, center);
        Require(presses == 1 && mouse.Captured is null, "disabling the dial cancels capture without a confirmation");
        navigation.IsEnabled = true;
        navigation.Focus();
        SendKey(navigation, Key.Space, true);
        SendKey(navigation, Key.Space, true);
        SendKey(navigation, Key.Space, false);
        Require(presses == 2, "a keyboard short press confirms once despite repeated key-down events");
        SendKey(navigation, Key.Enter, true);
        selector.Focus();
        SendKey(navigation, Key.Enter, false);
        Require(presses == 2, "moving focus cancels a pending keyboard confirmation");
        rotations.Clear();
        Wheel(window, navigation, mouse, 1);
        SendKey(navigation, Key.Left, true);
        Require(rotations.SequenceEqual([1, -1]) && presses == 2, "the wheel and arrow keys each rotate one detent");
    }

    private static void VerifySelector(Window window, DeviceRotaryDial selector)
    {
        var requested = new List<int>();
        bool accept = true;
        selector.SelectedPositionRequested += position =>
        {
            requested.Add(position);
            if (accept) { selector.SetSelectedPosition(position); }
        };
        var mouse = new Pointer(183, PointerType.Mouse, true);
        Point center = Center(selector);
        Press(window, selector, mouse, Ring(selector, -70));
        Release(window, selector, mouse, Ring(selector, -70));
        Require(selector.SelectedPosition == 1 && selector.AngleDegrees == -70,
            "clicking a marked position selects that position directly");
        int requests = requested.Count;
        Press(window, selector, mouse, center);
        Release(window, selector, mouse, center);
        SendKey(selector, Key.Enter, true);
        SendKey(selector, Key.Enter, false);
        Require(requested.Count == requests, "the selector center and Enter do not cycle through modes");

        selector.SetSelectedPosition(2);
        Press(window, selector, mouse, Ring(selector, 0));
        Require(ReferenceEquals(mouse.Captured, selector), "a selector drag captures the pointer");
        Move(window, selector, mouse, Ring(selector, 70));
        Release(window, selector, mouse, Ring(selector, 70));
        Require(selector.SelectedPosition == 3 && mouse.Captured is null, "dragging the selector follows its finite positions");
        selector.SetSelectedPosition(4);
        requests = requested.Count;
        Wheel(window, selector, mouse, 1);
        SendKey(selector, Key.Right, true);
        Press(window, selector, mouse, Ring(selector, 140));
        Move(window, selector, mouse, Ring(selector, 175));
        Move(window, selector, mouse, Ring(selector, -140));
        Release(window, selector, mouse, Ring(selector, -140));
        Require(selector.SelectedPosition == 4 && requested.Count == requests,
            "wheel, keyboard and clockwise dragging stop at the final position without wrapping");
        selector.SetSelectedPosition(0);
        requests = requested.Count;
        Wheel(window, selector, mouse, -1);
        SendKey(selector, Key.Left, true);
        Press(window, selector, mouse, Ring(selector, -140));
        Move(window, selector, mouse, Ring(selector, -175));
        Move(window, selector, mouse, Ring(selector, 140));
        Release(window, selector, mouse, Ring(selector, 140));
        Require(selector.SelectedPosition == 0 && requested.Count == requests,
            "anticlockwise input stops at the first position without wrapping");

        selector.SetSelectedPosition(2);
        accept = false;
        Wheel(window, selector, mouse, 1);
        Require(requested[^1] == 3 && selector.SelectedPosition == 2 && selector.AngleDegrees == 0,
            "a rejected wheel request preserves the accepted indicator position");
        Press(window, selector, mouse, Ring(selector, 0));
        Move(window, selector, mouse, Ring(selector, -70));
        Release(window, selector, mouse, Ring(selector, -70));
        Require(requested[^1] == 1 && selector.SelectedPosition == 2 && selector.AngleDegrees == 0,
            "a rejected drag request leaves both the mode and its indicator unchanged");
        requests = requested.Count;
        selector.IsEnabled = false;
        SendKey(selector, Key.Right, true);
        Wheel(window, selector, mouse, 1);
        Press(window, selector, mouse, Ring(selector, 70));
        Release(window, selector, mouse, Ring(selector, 70));
        Require(requested.Count == requests && mouse.Captured is null, "a disabled selector cannot request a position");
    }

    private static Point Center(DeviceRotaryDial dial) => new(dial.Bounds.Width / 2, dial.Bounds.Height / 2);

    private static Point Ring(DeviceRotaryDial dial, double degrees)
    {
        double radians = degrees * Math.PI / 180;
        return Center(dial) + new Vector(Math.Sin(radians) * 37, -Math.Cos(radians) * 37);
    }

    private static Point Position(Window window, Control control, Point local) => control.TranslatePoint(local, window)!.Value;

    private static void Press(Window window, DeviceRotaryDial dial, Pointer pointer, Point local) =>
        dial.RaiseEvent(new PointerPressedEventArgs(dial, pointer, window, Position(window, dial, local), 0,
            new PointerPointProperties(pointer.Type == PointerType.Touch ? RawInputModifiers.None : RawInputModifiers.LeftMouseButton,
                PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));

    private static void Move(Window window, DeviceRotaryDial dial, Pointer pointer, Point local) =>
        dial.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, dial, pointer, window, Position(window, dial, local), 0,
            new PointerPointProperties(pointer.Type == PointerType.Touch ? RawInputModifiers.None : RawInputModifiers.LeftMouseButton,
                PointerUpdateKind.Other), KeyModifiers.None));

    private static void Release(Window window, DeviceRotaryDial dial, Pointer pointer, Point local) =>
        dial.RaiseEvent(new PointerReleasedEventArgs(dial, pointer, window, Position(window, dial, local), 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));

    private static void Wheel(Window window, DeviceRotaryDial dial, Pointer pointer, double delta) =>
        dial.RaiseEvent(new PointerWheelEventArgs(dial, pointer, window, Position(window, dial, Center(dial)), 0,
            new PointerPointProperties(), KeyModifiers.None, new Vector(0, delta)));

    private static void SendKey(DeviceRotaryDial dial, Key key, bool down) =>
        dial.RaiseEvent(new KeyEventArgs { RoutedEvent = down ? InputElement.KeyDownEvent : InputElement.KeyUpEvent, Key = key });

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException("Rotary dial: " + message); }
    }
}

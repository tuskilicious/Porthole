using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using USBControl.Core;

namespace USBControl.App.Services;

/// <summary>
/// Attached behavior: enables mouse-dragging of a tile inside the panel canvas.
/// Uses SetCurrentValue so the Canvas.Left/Top bindings stay alive and the tile
/// snaps to the grid when the controller pushes the persisted position back.
/// </summary>
public static class PanelDrag
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(PanelDrag), new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject obj) => (bool)obj.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject obj, bool value) => obj.SetValue(EnabledProperty, value);

    private sealed class DragState
    {
        public required Canvas Canvas;
        public required FrameworkElement CanvasChild;
        public Point Origin;
        public Point StartPos;
        public bool Moved;
    }

    private static readonly Dictionary<DependencyObject, DragState> Active = new();

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement el)
            return;

        if ((bool)e.NewValue)
        {
            el.MouseLeftButtonDown += OnDown;
            el.MouseMove += OnMove;
            el.MouseLeftButtonUp += OnUp;
            el.MouseLeave += OnLeave;
        }
        else
        {
            el.MouseLeftButtonDown -= OnDown;
            el.MouseMove -= OnMove;
            el.MouseLeftButtonUp -= OnUp;
            el.MouseLeave -= OnLeave;
            Active.Remove(el);
        }
    }

    private static void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement el)
            return;

        // The Canvas.Left/Top live on the direct child of the canvas (the
        // ContentPresenter), not on the templated tile itself.
        var canvas = FindCanvas(el, out var canvasChild);
        if (canvas is null || canvasChild is null)
            return;

        Active[el] = new DragState
        {
            Canvas = canvas,
            CanvasChild = canvasChild,
            Origin = e.GetPosition(canvas),
            StartPos = new Point(GetX(canvasChild), GetY(canvasChild)),
            Moved = false,
        };
        el.CaptureMouse();
    }

    private static void OnMove(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement el || !Active.TryGetValue(el, out var st))
            return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndDrag(el, commit: true);
            return;
        }

        var p = e.GetPosition(st.Canvas);
        var dx = p.X - st.Origin.X;
        var dy = p.Y - st.Origin.Y;
        if (!st.Moved && Math.Abs(dx) < 3 && Math.Abs(dy) < 3)
            return;

        st.Moved = true;
        st.CanvasChild.SetCurrentValue(Canvas.LeftProperty,
            Math.Clamp(st.StartPos.X + dx, 0, PanelLayoutMath.CanvasWidth - PanelLayoutMath.TileWidth));
        st.CanvasChild.SetCurrentValue(Canvas.TopProperty,
            Math.Clamp(st.StartPos.Y + dy, 0, PanelLayoutMath.CanvasHeight - PanelLayoutMath.TileHeight));
        e.Handled = true;
    }

    private static void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement el && Active.TryGetValue(el, out _))
        {
            EndDrag(el, commit: true);
            e.Handled = true;
        }
    }

    private static void OnLeave(object sender, MouseEventArgs e)
    {
        // Lost the mouse mid-drag (alt-tab, capture stolen): keep last position.
        if (sender is FrameworkElement el && Active.TryGetValue(el, out var st) && st.Moved && e.LeftButton != MouseButtonState.Pressed)
            EndDrag(el, commit: true);
    }

    private static void EndDrag(FrameworkElement el, bool commit)
    {
        if (!Active.Remove(el, out var st))
            return;

        if (el.IsMouseCaptured)
            el.ReleaseMouseCapture();

        if (!commit || !st.Moved)
            return;

        var x = GetX(st.CanvasChild);
        var y = GetY(st.CanvasChild);
        if (st.CanvasChild.DataContext is PortViewModel vm && double.IsFinite(x) && double.IsFinite(y))
            vm.Controller.MovePortTo(vm, x, y);
    }

    private static double GetX(FrameworkElement el) =>
        el.ReadLocalValue(Canvas.LeftProperty) is double d && double.IsFinite(d) ? d : 0;

    private static double GetY(FrameworkElement el) =>
        el.ReadLocalValue(Canvas.TopProperty) is double d && double.IsFinite(d) ? d : 0;

    private static Canvas? FindCanvas(FrameworkElement el, out FrameworkElement? canvasChild)
    {
        DependencyObject current = el;
        while (true)
        {
            var parent = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
            if (parent is null)
            {
                canvasChild = null;
                return null;
            }
            if (parent is Canvas c)
            {
                canvasChild = current as FrameworkElement;
                return c;
            }
            current = parent;
        }
    }
}

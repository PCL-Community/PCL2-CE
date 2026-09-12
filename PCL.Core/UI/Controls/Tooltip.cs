using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace PCL.Core.UI.Controls;

public static class Tooltip
{
    #region Attached Properties

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(Tooltip), new PropertyMetadata(true));

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static readonly DependencyProperty FollowCursorProperty = DependencyProperty.RegisterAttached(
        "FollowCursor", typeof(bool), typeof(Tooltip), new PropertyMetadata(true));

    public static void SetFollowCursor(DependencyObject element, bool value) =>
        element.SetValue(FollowCursorProperty, value);

    public static bool GetFollowCursor(DependencyObject element) =>
        (bool)element.GetValue(FollowCursorProperty);

    #endregion

    #region Constants

    private const double ScaleClosed = 0.97;
    private const double ShadowBlur = 18;
    private const double ShadowAlpha = 0.15;
    private const int MaxContentWidth = 676;
    private const double TipFontSize = 12.5;
    private const double TipLineHeight = 17;
    private const int AnimLength = 80;
    private const int AnimExit = 80;

    private static readonly Thickness _InnerPad = new(12, 10, 12, 10);
    private static readonly DropShadowEffect _Shadow = new()
    {
        Opacity = ShadowAlpha,
        BlurRadius = ShadowBlur,
        ShadowDepth = 0,
        Color = Colors.Black
    };

    #endregion

    #region Per-Element Bookkeeping (Attached)

    private static readonly DependencyProperty _KeyCombo = DependencyProperty.RegisterAttached(
        "KeyCombo", typeof(bool), typeof(Tooltip), new PropertyMetadata(false));

    #endregion

    #region Global State

    private static bool _running;
    private static int _gen;
    private static bool _closing;
    private static Point _cursor;
    private static FrameworkElement? _target;
    private static Popup? _flyout;
    private static Border? _shell;
    private static ScaleTransform? _scaler;
    private static Storyboard? _openStory;
    private static Storyboard? _closeStory;
    private static DispatcherTimer? _latch;
    private static HwndSourceHook? _transparentHook;

    private static readonly MouseEventHandler _OnEnterHandler = OnEnter;
    private static readonly MouseEventHandler _OnMoveHandler = OnMove;
    private static readonly MouseEventHandler _OnLeaveHandler = OnLeave;
    private static readonly MouseButtonEventHandler _OnReleaseHandler = OnRelease;
    private static readonly ToolTipEventHandler _OnOpeningHandler = OnOpening;
    private static readonly RoutedEventHandler _OnUnloadedHandler = OnUnloaded;
    private static readonly RoutedEventHandler _OnComboLoadedHandler = OnComboInit;
    private static readonly MouseButtonEventHandler _OnComboMouseDownHandler = OnComboInit;

    #endregion

    #region Entry Point

    public static void Enable()
    {
        if (_running) return;
        _running = true;

        _Shadow.Freeze();

        _PrebuildStoryboards();

        EventManager.RegisterClassHandler(typeof(FrameworkElement),
            UIElement.MouseEnterEvent, _OnEnterHandler, true);
        EventManager.RegisterClassHandler(typeof(FrameworkElement),
            UIElement.MouseMoveEvent, _OnMoveHandler, true);
        EventManager.RegisterClassHandler(typeof(FrameworkElement),
            UIElement.MouseLeaveEvent, _OnLeaveHandler, true);
        EventManager.RegisterClassHandler(typeof(FrameworkElement),
            UIElement.PreviewMouseUpEvent, _OnReleaseHandler, true);
        EventManager.RegisterClassHandler(typeof(FrameworkElement),
            ToolTipService.ToolTipOpeningEvent, _OnOpeningHandler, true);
        EventManager.RegisterClassHandler(typeof(FrameworkElement),
            FrameworkElement.UnloadedEvent, _OnUnloadedHandler, true);

        EventManager.RegisterClassHandler(typeof(ComboBox),
            FrameworkElement.LoadedEvent, _OnComboLoadedHandler, true);
        EventManager.RegisterClassHandler(typeof(ComboBox),
            UIElement.PreviewMouseDownEvent, _OnComboMouseDownHandler, true);

        EventManager.RegisterClassHandler(typeof(Window),
            UIElement.MouseLeaveEvent, new MouseEventHandler(OnWindowLeave), true);
    }

    private static void OnWindowLeave(object s, MouseEventArgs e)
    {
        if (_target is not null)
            _WindDown();
    }

    public static void Disable()
    {
        if (!_running) return;
        _running = false;
        _Hush();
    }

    public static void Dismiss()
    {
        _WindDown();
    }

    #endregion

    #region Storyboard Setup

    private static void _PrebuildStoryboards()
    {
        static DoubleAnimation MakeAnim(double to, string prop, int ms)
        {
            var a = new DoubleAnimation(to, new Duration(TimeSpan.FromMilliseconds(ms)))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTargetProperty(a, new PropertyPath(prop));
            return a;
        }

        _openStory = new Storyboard();
        _openStory.Children.Add(MakeAnim(1, nameof(UIElement.Opacity), AnimLength));
        _openStory.Children.Add(MakeAnim(1, "RenderTransform.ScaleX", AnimLength));
        _openStory.Children.Add(MakeAnim(1, "RenderTransform.ScaleY", AnimLength));

        _closeStory = new Storyboard();
        _closeStory.Children.Add(MakeAnim(0, nameof(UIElement.Opacity), AnimExit));
        _closeStory.Children.Add(MakeAnim(ScaleClosed, "RenderTransform.ScaleX", AnimExit));
        _closeStory.Children.Add(MakeAnim(ScaleClosed, "RenderTransform.ScaleY", AnimExit));
    }

    #endregion

    #region Event Trampolines

    private static void OnEnter(object s, MouseEventArgs e)
    {
        if (!_running || s is not FrameworkElement fe) return;
        fe.Dispatcher.BeginInvoke(() => _TryClaim(fe));
    }

    private static bool _IsCursorPlaced(FrameworkElement el) =>
        GetFollowCursor(el) && ToolTipService.GetPlacement(el) is PlacementMode.Mouse or PlacementMode.MousePoint;

    private static void OnMove(object s, MouseEventArgs e)
    {
        if (!_running || s is not FrameworkElement fe) return;

        if (Mouse.LeftButton == MouseButtonState.Pressed)
        {
            if (_target is not null)
            {
                _cursor = Mouse.GetPosition(_target);
                if (_IsCursorPlaced(_target) && _flyout is { IsOpen: true })
                    _PlaceNear(_target, _cursor);
            }
            else if (_flyout is { IsOpen: true, PlacementTarget: FrameworkElement ft } && _IsCursorPlaced(ft))
            {
                _cursor = Mouse.GetPosition(ft);
                _PlaceNear(ft, _cursor);
            }
            return;
        }

        _TryClaim(fe);

        if (_target is not null)
        {
            _cursor = Mouse.GetPosition(_target);
            if (_IsCursorPlaced(_target) && _flyout is { IsOpen: true })
                _PlaceNear(_target, _cursor);
        }
        else if (_flyout is { IsOpen: true, PlacementTarget: FrameworkElement ft } && _IsCursorPlaced(ft))
        {
            _cursor = Mouse.GetPosition(ft);
            _PlaceNear(ft, _cursor);
        }
    }

    private static void OnLeave(object s, MouseEventArgs e)
    {
        if (!_running || s is not FrameworkElement fe || !ReferenceEquals(fe, _target)) return;

        if (_PointInside(fe, Mouse.GetPosition(fe)))
        {
            e.Handled = true;
            return;
        }

        var next = _SeekOwner(_Over());
        if (next is not null && !ReferenceEquals(next, _target))
        {
            _StartCycle(next, Mouse.GetPosition(next));
            return;
        }

        _WindDown();
    }

    private static void OnRelease(object s, MouseButtonEventArgs e)
    {
        if (!_running || s is not FrameworkElement fe) return;
        fe.Dispatcher.BeginInvoke(() =>
        {
            if (_target is null) return;
            var owner = _SeekOwner(fe);
            if (owner is null)
                _WindDown();
            else
                _StartCycle(owner, Mouse.GetPosition(owner));
        }, DispatcherPriority.Input);
    }

    private static void OnOpening(object s, ToolTipEventArgs e)
    {
        if (!_running || s is not FrameworkElement fe || !_Eligible(fe) || !_FetchContent(fe)) return;
        e.Handled = true;

        if (_DragHush(fe))
        {
            _Hush();
            return;
        }
        
        if (fe.IsEnabled)
        {
            if (ReferenceEquals(_target, fe) && _flyout is { IsOpen: true })
                return;

            if (!ReferenceEquals(_target, fe)) _Hush();
            _target = fe;
            _latch?.Stop();
            _latch = null;
            _cursor = Mouse.GetPosition(fe);
            _PopUp(fe, _cursor);
            return;
        }

        if (!ReferenceEquals(_target, fe)) _Hush();
        _target = fe;
        _latch?.Stop();
        _cursor = Mouse.GetPosition(fe);
        _PopUp(fe, _cursor);
    }

    private static void OnUnloaded(object s, RoutedEventArgs e)
    {
        if (!_running) return;
        if (s is FrameworkElement fe && ReferenceEquals(fe, _target))
            _WindDown();
    }

    #endregion

    #region Owner Resolution

    private static void _TryClaim(FrameworkElement pivot)
    {
        var owner = _SeekOwner(_Over());
        var candidate = owner ?? pivot;

        if (ReferenceEquals(candidate, pivot) && !_PointInside(pivot, Mouse.GetPosition(pivot)))
            return;

        if (!_Eligible(candidate) || !_FetchContent(candidate))
        {
            if (_target is not null)
                _WindDown();
            return;
        }

        if (_DragHush(candidate))
        {
            if (_target is not null &&
                _Captured() is { } cap &&
                _ShareAncestor(cap, _target) &&
                _PointInside(_target, Mouse.GetPosition(_target)))
                return;

            _Hush();
            return;
        }
        
        _StartCycle(candidate, Mouse.GetPosition(candidate));
    }

    private static DependencyObject? _Over() => Mouse.DirectlyOver as DependencyObject;
    private static DependencyObject? _Captured() => Mouse.Captured as DependencyObject;

    private static FrameworkElement? _SeekOwner(DependencyObject? leaf)
    {
        for (var cur = leaf; cur is not null; cur = cur is Visual ? VisualTreeHelper.GetParent(cur) : null)
        {
            if (cur is FrameworkElement fe && _Eligible(fe) && _FetchContent(fe))
                return fe;
        }
        return null;
    }

    private static bool _Eligible(FrameworkElement fe) =>
        GetIsEnabled(fe) && ToolTipService.GetIsEnabled(fe) &&
        (fe.IsEnabled || ToolTipService.GetShowOnDisabled(fe));

    private static bool _FetchContent(FrameworkElement src)
    {
        var raw = src.ToolTip;
        if (raw is null) return false;

        var payload = raw is ToolTip tip ? tip.Content : raw;
        return payload is not null && (payload is not string s || s.Length > 0);
    }

    private static bool _DragHush(FrameworkElement? candidate)
    {
        if (Mouse.LeftButton == MouseButtonState.Pressed || Mouse.Captured is null) return false;
        if (candidate is null) return true;
        var cap = _Captured();
        if (cap is null) return true;
        return !_ShareAncestor(cap, candidate);
    }

    private static bool _PointInside(FrameworkElement el, Point p) =>
        p.X >= 0 && p.Y >= 0 && p.X <= el.ActualWidth && p.Y <= el.ActualHeight;

    private static bool _ShareAncestor(DependencyObject a, DependencyObject b)
    {
        if (ReferenceEquals(a, b)) return true;
        
        if (a is ComboBox comboA && b is ComboBoxItem itemB &&
            ItemsControl.ItemsControlFromItemContainer(itemB) == comboA)
            return true;
        if (b is ComboBox comboB && a is ComboBoxItem itemA &&
            ItemsControl.ItemsControlFromItemContainer(itemA) == comboB)
            return true;

        for (var cur = VisualTreeHelper.GetParent(b); cur is not null; cur = VisualTreeHelper.GetParent(cur))
            if (ReferenceEquals(cur, a)) return true;
        for (var cur = VisualTreeHelper.GetParent(a); cur is not null; cur = VisualTreeHelper.GetParent(cur))
            if (ReferenceEquals(cur, b)) return true;
        return false;
    }

    #endregion

    #region Cycle Management

    private static void _StartCycle(FrameworkElement target, Point pt)
    {
        if (!_Eligible(target) || !_FetchContent(target)) return;
        if (_DragHush(target))
        {
            _Hush();
            return;
        }

        _Stitch(target as ComboBox);

        if (ReferenceEquals(_target, target))
        {
            _cursor = pt;
            if (_flyout is not { IsOpen: true } && _latch is null)
                _KickTimer(target);
            return;
        }

        // Tooltip 已打开时切换到新元素，先淡出旧内容再淡入新内容
        if (_flyout is { IsOpen: true })
        {
            _closing = false;
            _target = target;
            _cursor = pt;
            var mark = ++_gen;
            var sb = _closeStory!.Clone();
            sb.Completed += (_, _) =>
            {
                if (mark == _gen)
                {
                    _flyout.IsOpen = false;
                    _PopUp(target, pt);
                }
                sb.Remove(_shell!);
            };
            _shell!.BeginStoryboard(sb);
            return;
        }

        _Hush();
        _target = target;
        _cursor = pt;
        _KickTimer(target);
    }

    private static void _KickTimer(FrameworkElement target)
    {
        _latch?.Stop();

        var ms = Math.Max(0, ToolTipService.GetInitialShowDelay(target));
        if (ms == 0)
        {
            _PopUp(target, _cursor);
            return;
        }

        var mark = ++_gen;
        _latch = new DispatcherTimer(
            TimeSpan.FromMilliseconds(ms),
            DispatcherPriority.Normal,
            (_, _) =>
            {
                _latch?.Stop();
                if (mark == _gen && _target is not null)
                    _PopUp(_target, _cursor);
            },
            target.Dispatcher);
    }

    private static void _PopUp(FrameworkElement target, Point pt)
    {
        if (!ReferenceEquals(target, _target)) return;

        if (_flyout is null)
            _BuildUi();

        _flyout!.PlacementTarget = target;
        _PlaceNear(target, pt);

        _shell!.DataContext = (target.ToolTip as ToolTip)?.DataContext ?? target.DataContext;
        _shell.FlowDirection = target.FlowDirection;

        _RenderInside(target);

        _gen++;
        _shell.BeginStoryboard(_openStory!);
        _flyout.IsOpen = true;
    }

    private static void _BuildUi()
    {
        _scaler = new ScaleTransform(ScaleClosed, ScaleClosed);
        _shell = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            MaxWidth = 700,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true,
            RenderTransform = _scaler,
            RenderTransformOrigin = new Point(0, 0),
            Effect = _Shadow
        };
        _shell.SetResourceReference(Border.BackgroundProperty, "ColorBrushWhite");
        _shell.SetResourceReference(Border.BorderBrushProperty, "ColorBrushGray5");

        var wrap = new Grid
        {
            Margin = new Thickness(ShadowBlur + 1),
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };
        wrap.Children.Add(_shell);

        _flyout = new Popup
        {
            AllowsTransparency = true,
            IsHitTestVisible = false,
            StaysOpen = true,
            PopupAnimation = PopupAnimation.None,
            Placement = PlacementMode.Relative,
            Child = wrap
        };

        // 预创建 HWND 并挂载鼠标穿透钩子，Popup 不接收任何鼠标消息，全部透传到下层窗口
        const int WM_NCHITTEST = 0x0084;
        const int HTTRANSPARENT = -1;
        _transparentHook = (_, msg, _, _, ref handled) =>
        {
            if (msg == WM_NCHITTEST)
            {
                handled = true;
                return HTTRANSPARENT;
            }
            return IntPtr.Zero;
        };
        _flyout.IsOpen = true;
        _AttachTransparentHook();
        _flyout.IsOpen = false;
        _flyout.Opened += (_, _) => _AttachTransparentHook();
    }

    private static void _AttachTransparentHook()
    {
        if (_transparentHook is null || _flyout?.Child is not UIElement child) return;
        var src = (HwndSource?)PresentationSource.FromVisual(child);
        src?.AddHook(_transparentHook);
    }

    private static void _RenderInside(FrameworkElement owner)
    {
        _shell!.Child = null;

        var raw = owner.ToolTip;
        var tip = raw as ToolTip;
        var content = tip?.Content ?? raw;

        if (content is null || content is string { Length: 0 })
            return;

        var hasTpl = tip?.ContentTemplate is not null || tip?.ContentTemplateSelector is not null;
        var tipW = tip is { Width: > 0 } && !double.IsNaN(tip.Width) ? tip.Width : MaxContentWidth;

        if (content is string text && !hasTpl)
        {
            var tb = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Margin = _InnerPad,
                FontSize = TipFontSize,
                LineHeight = TipLineHeight,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                MaxWidth = tipW
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush1");
            _shell.Child = tb;
        }
        else
        {
            _shell.Child = new ContentPresenter
            {
                Content = content,
                ContentTemplate = tip?.ContentTemplate,
                ContentTemplateSelector = tip?.ContentTemplateSelector,
                ContentStringFormat = tip?.ContentStringFormat,
                Margin = _InnerPad,
                MaxWidth = tipW
            };
        }
    }

    private static void _PlaceNear(FrameworkElement target, Point pt)
    {
        _flyout!.PlacementTarget = target;
        var mode = ToolTipService.GetPlacement(target);

        if (mode is PlacementMode.Mouse)
        {
            _flyout.Placement = PlacementMode.Left;
            _flyout.PlacementRectangle = default;
            _flyout.HorizontalOffset = Math.Round(pt.X + 15 + ToolTipService.GetHorizontalOffset(target));
            _flyout.VerticalOffset = Math.Round(pt.Y + 15 + ToolTipService.GetVerticalOffset(target));
        }
        else if (mode is PlacementMode.MousePoint)
        {
            _flyout.Placement = PlacementMode.Relative;
            _flyout.PlacementRectangle = default;
            _flyout.HorizontalOffset = Math.Round(pt.X + ToolTipService.GetHorizontalOffset(target));
            _flyout.VerticalOffset = Math.Round(pt.Y + ToolTipService.GetVerticalOffset(target));
        }
        else
        {
            _flyout.Placement = mode;
            _flyout.HorizontalOffset = ToolTipService.GetHorizontalOffset(target);
            _flyout.VerticalOffset = ToolTipService.GetVerticalOffset(target);
            _flyout.PlacementRectangle = ToolTipService.GetPlacementRectangle(target);
        }
    }

    private static void _WindDown()
    {
        if (_closing) return;
        _closing = true;

        _latch?.Stop();
        _latch = null;
        _target = null;

        if (_flyout is not { IsOpen: true } || _shell is null)
        {
            _Hush();
            return;
        }

        var mark = ++_gen;
        var sb = _closeStory!.Clone();
        sb.Completed += (_, _) =>
        {
            if (mark == _gen) _Hush();
            sb.Remove(_shell);
        };
        _shell.BeginStoryboard(sb);
    }

    private static void _Hush()
    {
        _latch?.Stop();
        _latch = null;
        _closing = false;
        _target = null;
        _gen++;

        if (_flyout is not null)
            _flyout.IsOpen = false;

        if (_shell is not null)
        {
            _shell.BeginAnimation(UIElement.OpacityProperty, null);
            _shell.Child = null;
        }

        if (_scaler is not null)
        {
            _scaler.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _scaler.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }
    }

    #endregion

    #region ComboBox Hook

    private static void OnComboInit(object s, RoutedEventArgs e) => _Stitch(s as ComboBox);
    private static void OnComboInit(object s, MouseButtonEventArgs e) => _Stitch(s as ComboBox);

    private static void _Stitch(ComboBox? box)
    {
        if (box is null || (bool)box.GetValue(_KeyCombo)) return;
        box.SetValue(_KeyCombo, true);
        box.DropDownOpened += (_, _) =>
        {
            if (_target is not null) _WindDown();
        };
    }

    #endregion
}

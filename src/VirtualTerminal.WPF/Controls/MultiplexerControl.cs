using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace VirtualTerminal.Controls;

/// <summary>
/// Event arguments for multiplexer pane state changes.
/// </summary>
public sealed class MultiplexerPaneEventArgs : EventArgs
{
    /// <summary>Gets the pane associated with the event.</summary>
    public UIElement Pane { get; }

    /// <summary>Initializes a new instance of <see cref="MultiplexerPaneEventArgs"/>.</summary>
    public MultiplexerPaneEventArgs(UIElement pane) => Pane = pane;
}

/// <summary>
/// Generic desktop multiplexer control that tiles and splits any WPF <see cref="UIElement"/>
/// into arbitrary horizontal/vertical layouts with smooth expansion animations,
/// comprehensive public APIs, and built-in command bindings.
/// </summary>
public class MultiplexerControl : Grid
{
    private UIElement? _root;
    private UIElement? _activeControl;
    private readonly ContentPresenter _rootHost;

    /// <summary>Event raised when the currently active pane changes.</summary>
    public event EventHandler<UIElement?>? ActiveControlChanged;

    /// <summary>Event raised when a pane is opened / added to the multiplexer.</summary>
    public event EventHandler<MultiplexerPaneEventArgs>? PaneOpened;

    /// <summary>Event raised when a pane is closed / removed from the multiplexer.</summary>
    public event EventHandler<MultiplexerPaneEventArgs>? PaneClosed;

    /// <summary>Factory to generate a new pane when commands (like Split) are invoked without an explicit element.</summary>
    public Func<UIElement>? NewPaneFactory { get; set; }

    /// <summary>Gets or sets whether smooth expansion animation is enabled when splitting panes (default true).</summary>
    public bool IsAnimationEnabled { get; set; } = true;

    /// <summary>Gets or sets the duration of the split opening animation (default 180ms).</summary>
    public TimeSpan AnimationDuration { get; set; } = TimeSpan.FromMilliseconds(180);

    /// <summary>Gets or sets the default split ratio when splitting (default 0.5).</summary>
    public double DefaultSplitRatio { get; set; } = 0.5;

    /// <summary>Command to split the active pane vertically.</summary>
    public ICommand SplitVerticalCommand { get; }

    /// <summary>Command to split the active pane horizontally.</summary>
    public ICommand SplitHorizontalCommand { get; }

    /// <summary>Command to close the currently active pane.</summary>
    public ICommand CloseActiveCommand { get; }

    /// <summary>Command to move focus to the next pane.</summary>
    public ICommand FocusNextCommand { get; }

    /// <summary>Command to move focus to the previous pane.</summary>
    public ICommand FocusPreviousCommand { get; }

    /// <summary>Initializes a new instance of <see cref="MultiplexerControl"/>.</summary>
    public MultiplexerControl()
    {
        _rootHost = new ContentPresenter
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        Children.Add(_rootHost);

        // Command initializations
        SplitVerticalCommand = new RelayCommand(_ => SplitActive(NewPaneFactory?.Invoke(), Orientation.Vertical), _ => PaneCount > 0 || NewPaneFactory != null);
        SplitHorizontalCommand = new RelayCommand(_ => SplitActive(NewPaneFactory?.Invoke(), Orientation.Horizontal), _ => PaneCount > 0 || NewPaneFactory != null);
        CloseActiveCommand = new RelayCommand(_ => CloseActive(), _ => ActiveControl != null);
        FocusNextCommand = new RelayCommand(_ => FocusNext(), _ => PaneCount > 1);
        FocusPreviousCommand = new RelayCommand(_ => FocusPrevious(), _ => PaneCount > 1);

        // Register RoutedUICommand bindings
        CommandBindings.Add(new CommandBinding(MultiplexerCommands.SplitVertical, (s, e) => { SplitActive(NewPaneFactory?.Invoke(), Orientation.Vertical); e.Handled = true; }));
        CommandBindings.Add(new CommandBinding(MultiplexerCommands.SplitHorizontal, (s, e) => { SplitActive(NewPaneFactory?.Invoke(), Orientation.Horizontal); e.Handled = true; }));
        CommandBindings.Add(new CommandBinding(MultiplexerCommands.CloseActive, (s, e) => { CloseActive(); e.Handled = true; }));
        CommandBindings.Add(new CommandBinding(MultiplexerCommands.FocusNext, (s, e) => { FocusNext(); e.Handled = true; }));
        CommandBindings.Add(new CommandBinding(MultiplexerCommands.FocusPrevious, (s, e) => { FocusPrevious(); e.Handled = true; }));

        // Track focus across all descendant elements
        AddHandler(UIElement.GotFocusEvent, new RoutedEventHandler(OnDescendantGotFocus));
    }

    /// <summary>Gets or sets the root element of the multiplexer split tree.</summary>
    public UIElement? Root
    {
        get => _root;
        set
        {
            if (!ReferenceEquals(_root, value))
            {
                if (_root != null)
                    TerminalSplitControl.DisconnectFromParent(_root);

                _root = value;
                _rootHost.Content = value;

                if (value != null && (ActiveControl == null || !ContainsPane(value, ActiveControl)))
                {
                    ActiveControl = FindFirstLeaf(value);
                }
            }
        }
    }

    /// <summary>Gets or sets the currently active / focused pane element.</summary>
    public UIElement? ActiveControl
    {
        get => _activeControl;
        set
        {
            if (!ReferenceEquals(_activeControl, value))
            {
                _activeControl = value;
                ActiveControlChanged?.Invoke(this, value);
            }
        }
    }

    /// <summary>Gets the list of all active leaf panes currently in the multiplexer.</summary>
    public IReadOnlyList<UIElement> Panes
    {
        get
        {
            var list = new List<UIElement>();
            if (_root is TerminalSplitControl split)
                split.CollectLeaves(list);
            else if (_root != null)
                list.Add(_root);

            return list;
        }
    }

    /// <summary>Gets the number of active leaf panes in the multiplexer.</summary>
    public int PaneCount => Panes.Count;

    /// <summary>
    /// Splits the currently active pane horizontally or vertically with the specified new control.
    /// </summary>
    public bool SplitActive(UIElement? newControl, Orientation orientation, double ratio = 0.5, bool? animate = null)
    {
        newControl ??= NewPaneFactory?.Invoke();
        if (newControl == null)
            return false;

        var target = ActiveControl ?? FindFirstLeaf(_root);
        return Split(target, newControl, orientation, ratio, animate);
    }

    /// <summary>
    /// Splits the target pane with a new control in the specified orientation.
    /// </summary>
    public bool Split(UIElement? target, UIElement newControl, Orientation orientation, double ratio = 0.5, bool? animate = null)
    {
        bool useAnimation = animate ?? IsAnimationEnabled;
        TimeSpan duration = AnimationDuration;

        TerminalSplitControl.DisconnectFromParent(newControl);

        if (_root == null)
        {
            Root = newControl;
            ActiveControl = newControl;
            PaneOpened?.Invoke(this, new MultiplexerPaneEventArgs(newControl));
            return true;
        }

        if (target == null || ReferenceEquals(_root, target))
        {
            // Root is a single leaf; wrap it in a new split
            var existing = _root;
            _rootHost.Content = null;
            TerminalSplitControl.DisconnectFromParent(existing);

            var newSplit = new TerminalSplitControl(existing, newControl, orientation) { SplitRatio = ratio };
            Root = newSplit;

            if (useAnimation)
                newSplit.AnimateSplit(0.005, ratio, duration);

            ActiveControl = newControl;
            FocusControl(newControl);
            PaneOpened?.Invoke(this, new MultiplexerPaneEventArgs(newControl));
            return true;
        }

        if (_root is TerminalSplitControl rootSplit)
        {
            if (rootSplit.SplitChild(target, newControl, orientation, ratio, useAnimation, duration))
            {
                ActiveControl = newControl;
                FocusControl(newControl);
                PaneOpened?.Invoke(this, new MultiplexerPaneEventArgs(newControl));
                return true;
            }

            // Fallback: wrap entire root
            _rootHost.Content = null;
            TerminalSplitControl.DisconnectFromParent(rootSplit);
            var newSplit = new TerminalSplitControl(rootSplit, newControl, orientation) { SplitRatio = ratio };
            Root = newSplit;

            if (useAnimation)
                newSplit.AnimateSplit(0.005, ratio, duration);

            ActiveControl = newControl;
            FocusControl(newControl);
            PaneOpened?.Invoke(this, new MultiplexerPaneEventArgs(newControl));
            return true;
        }

        return false;
    }

    /// <summary>Closes the currently active pane.</summary>
    public bool CloseActive()
    {
        if (ActiveControl == null)
            return false;

        return Close(ActiveControl);
    }

    /// <summary>Closes the specified pane and collapses the parent split tree.</summary>
    public bool Close(UIElement controlToClose)
    {
        if (_root == null)
            return false;

        if (ReferenceEquals(_root, controlToClose))
        {
            _rootHost.Content = null;
            TerminalSplitControl.DisconnectFromParent(_root);
            _root = null;
            ActiveControl = null;
            PaneClosed?.Invoke(this, new MultiplexerPaneEventArgs(controlToClose));
            return true;
        }

        if (_root is TerminalSplitControl rootSplit)
        {
            var remaining = rootSplit.CloseChild(controlToClose);
            if (remaining != null && !ReferenceEquals(remaining, rootSplit))
            {
                TerminalSplitControl.DisconnectFromParent(remaining);
                Root = remaining;
            }

            var next = FindFirstLeaf(_root);
            ActiveControl = next;
            FocusControl(next);
            PaneClosed?.Invoke(this, new MultiplexerPaneEventArgs(controlToClose));
            return true;
        }

        return false;
    }

    /// <summary>Moves focus to the next pane in layout sequence.</summary>
    public void FocusNext()
    {
        var panes = Panes;
        if (panes.Count <= 1)
            return;

        int idx = IndexOf(panes, _activeControl);
        int nextIdx = (idx + 1) % panes.Count;
        ActiveControl = panes[nextIdx];
        FocusControl(panes[nextIdx]);
    }

    /// <summary>Moves focus to the previous pane in layout sequence.</summary>
    public void FocusPrevious()
    {
        var panes = Panes;
        if (panes.Count <= 1)
            return;

        int idx = IndexOf(panes, _activeControl);
        int prevIdx = (idx - 1 + panes.Count) % panes.Count;
        ActiveControl = panes[prevIdx];
        FocusControl(panes[prevIdx]);
    }

    /// <summary>Swaps two pane controls in the layout tree.</summary>
    public bool SwapActiveWith(UIElement other)
    {
        if (ActiveControl == null || ReferenceEquals(ActiveControl, other))
            return false;

        if (_root is TerminalSplitControl rootSplit)
        {
            // Create a temp placeholder
            var placeholder = new Border();
            rootSplit.ReplaceChild(ActiveControl, placeholder);
            rootSplit.ReplaceChild(other, ActiveControl);
            rootSplit.ReplaceChild(placeholder, other);
            return true;
        }

        return false;
    }

    /// <summary>Clears all panes from the multiplexer.</summary>
    public void Clear()
    {
        if (_root != null)
        {
            _rootHost.Content = null;
            TerminalSplitControl.DisconnectFromParent(_root);
            _root = null;
            ActiveControl = null;
        }
    }

    /// <summary>Captures a serializable snapshot of the current multiplexer layout tree.</summary>
    public MultiplexerLayoutNode? GetLayoutState(Func<UIElement, string>? idSelector = null)
    {
        if (_root == null)
            return null;

        return BuildNode(_root, idSelector);
    }

    /// <summary>Restores the multiplexer layout tree from a saved state using a control factory.</summary>
    public bool RestoreLayoutState(MultiplexerLayoutNode layout, Func<string, UIElement> controlFactory)
    {
        Clear();
        var reconstructed = RestoreNode(layout, controlFactory);
        if (reconstructed != null)
        {
            Root = reconstructed;
            return true;
        }
        return false;
    }

    private static MultiplexerLayoutNode BuildNode(UIElement element, Func<UIElement, string>? idSelector)
    {
        if (element is TerminalSplitControl split)
        {
            return new MultiplexerLayoutNode
            {
                IsSplit = true,
                Orientation = split.Orientation,
                SplitRatio = split.SplitRatio,
                Child1 = split.Pane1 != null ? BuildNode(split.Pane1, idSelector) : null,
                Child2 = split.Pane2 != null ? BuildNode(split.Pane2, idSelector) : null,
            };
        }

        string id = idSelector?.Invoke(element) ?? (element as FrameworkElement)?.Name ?? element.GetType().Name;
        return new MultiplexerLayoutNode
        {
            IsSplit = false,
            PaneId = id,
        };
    }

    private static UIElement? RestoreNode(MultiplexerLayoutNode node, Func<string, UIElement> controlFactory)
    {
        if (node.IsSplit)
        {
            var c1 = node.Child1 != null ? RestoreNode(node.Child1, controlFactory) : null;
            var c2 = node.Child2 != null ? RestoreNode(node.Child2, controlFactory) : null;
            return new TerminalSplitControl(c1, c2, node.Orientation) { SplitRatio = node.SplitRatio };
        }

        return node.PaneId != null ? controlFactory(node.PaneId) : null;
    }

    private void OnDescendantGotFocus(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep)
        {
            var leaf = FindLeafAncestor(dep);
            if (leaf != null)
                ActiveControl = leaf;
        }
    }

    private UIElement? FindLeafAncestor(DependencyObject? current)
    {
        var panes = Panes;
        while (current != null)
        {
            if (current is UIElement ue && Contains(panes, ue))
                return ue;

            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static UIElement? FindFirstLeaf(UIElement? element)
    {
        if (element is TerminalSplitControl split)
            return split.FindActiveLeaf();

        return element;
    }

    private static bool ContainsPane(UIElement root, UIElement target)
    {
        if (ReferenceEquals(root, target))
            return true;

        if (root is TerminalSplitControl split)
        {
            return (split.Pane1 != null && ContainsPane(split.Pane1, target)) ||
                   (split.Pane2 != null && ContainsPane(split.Pane2, target));
        }

        return false;
    }

    private static int IndexOf(IReadOnlyList<UIElement> list, UIElement? item)
    {
        if (item == null) return -1;
        for (int i = 0; i < list.Count; i++)
            if (ReferenceEquals(list[i], item)) return i;
        return -1;
    }

    private static bool Contains(IReadOnlyList<UIElement> list, UIElement? item) => IndexOf(list, item) >= 0;

    private static void FocusControl(UIElement? element)
    {
        if (element is FrameworkElement fe)
            fe.Focus();
    }

    private sealed class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
        public void Execute(object? parameter) => _execute(parameter);
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}

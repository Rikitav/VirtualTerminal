using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace VirtualTerminal.Controls;

/// <summary>
/// Ready-to-use modern terminal multiplexer control providing multi-tab and resizable split-pane workspaces.
/// </summary>
public class TerminalMultiplexerControl : Grid
{
    private readonly TerminalTabControl _tabControl;
    private readonly StackPanel _actionsPanel;
    private TerminalControl? _activeTerminal;

    /// <summary>Event raised when the active terminal changes focus.</summary>
    public event EventHandler<TerminalControl?>? ActiveTerminalChanged;

    /// <summary>Factory method to create new <see cref="TerminalControl"/> instances when splitting or adding tabs.</summary>
    public Func<TerminalControl>? TerminalControlFactory { get; set; }

    /// <summary>Gets the underlying <see cref="TerminalTabControl"/>.</summary>
    public TerminalTabControl TabControl => _tabControl;

    /// <summary>Gets the currently active / focused <see cref="TerminalControl"/>.</summary>
    public TerminalControl? ActiveTerminal
    {
        get => _activeTerminal;
        private set
        {
            if (!ReferenceEquals(_activeTerminal, value))
            {
                _activeTerminal = value;
                ActiveTerminalChanged?.Invoke(this, value);
            }
        }
    }

    /// <summary>Initializes a new instance of <see cref="TerminalMultiplexerControl"/>.</summary>
    public TerminalMultiplexerControl()
    {
        _tabControl = new TerminalTabControl();
        _tabControl.NewTabContentFactory = () => CreateTerminal();

        // Overlay action buttons on the top right
        _actionsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 3, 6, 0),
        };

        var splitVerticalBtn = CreateActionButton("│", "Split Right (Vertical)", (s, e) => SplitActivePane(Orientation.Vertical));
        var splitHorizontalBtn = CreateActionButton("─", "Split Down (Horizontal)", (s, e) => SplitActivePane(Orientation.Horizontal));
        var closePaneBtn = CreateActionButton("✕", "Close Active Pane", (s, e) => CloseActivePane());

        _actionsPanel.Children.Add(splitVerticalBtn);
        _actionsPanel.Children.Add(splitHorizontalBtn);
        _actionsPanel.Children.Add(closePaneBtn);

        Children.Add(_tabControl);
        Children.Add(_actionsPanel);

        // Listen for descendant focus to track ActiveTerminal
        AddHandler(UIElement.GotFocusEvent, new RoutedEventHandler(OnDescendantGotFocus));

        // Create default first tab
        _tabControl.AddTab("Terminal 1", CreateTerminal());
    }

    /// <summary>Splits the currently active pane horizontally or vertically.</summary>
    /// <param name="orientation">Vertical for side-by-side panes, Horizontal for stacked panes.</param>
    /// <returns>The newly created <see cref="TerminalControl"/>.</returns>
    public TerminalControl? SplitActivePane(Orientation orientation)
    {
        var tab = _tabControl.SelectedTab;
        if (tab == null)
            return null;

        var currentActive = _activeTerminal ?? FindFirstTerminal(tab.Content);
        var newTerminal = CreateTerminal();

        if (currentActive == null || tab.Content == null || ReferenceEquals(tab.Content, currentActive))
        {
            // The entire tab was a single terminal (or empty); wrap it into a split
            var existing = tab.Content ?? currentActive ?? CreateTerminal();
            TerminalSplitControl.DisconnectFromParent(existing);
            TerminalSplitControl.DisconnectFromParent(newTerminal);

            var split = new TerminalSplitControl(existing, newTerminal, orientation);
            _tabControl.SetTabContent(tab, split);
        }
        else if (tab.Content is TerminalSplitControl rootSplit)
        {
            if (!rootSplit.SplitChild(currentActive, newTerminal, orientation))
            {
                TerminalSplitControl.DisconnectFromParent(rootSplit);
                TerminalSplitControl.DisconnectFromParent(newTerminal);
                var split = new TerminalSplitControl(rootSplit, newTerminal, orientation);
                _tabControl.SetTabContent(tab, split);
            }
            else
            {
                _tabControl.RefreshContent();
            }
        }

        ActiveTerminal = newTerminal;
        newTerminal.Focus();
        return newTerminal;
    }

    /// <summary>Closes the currently active terminal pane.</summary>
    public void CloseActivePane()
    {
        var tab = _tabControl.SelectedTab;
        if (tab == null)
            return;

        var target = _activeTerminal ?? FindFirstTerminal(tab.Content);
        if (target == null)
            return;

        if (ReferenceEquals(tab.Content, target))
        {
            // Target is the sole terminal in the tab; close the entire tab
            _tabControl.CloseTab(tab);
            return;
        }

        if (tab.Content is TerminalSplitControl rootSplit)
        {
            UIElement? remaining = rootSplit.CloseChild(target);
            if (remaining != null && !ReferenceEquals(remaining, rootSplit))
            {
                TerminalSplitControl.DisconnectFromParent(remaining);
                _tabControl.SetTabContent(tab, remaining);
            }
            else
            {
                _tabControl.RefreshContent();
            }

            var nextActive = FindFirstTerminal(tab.Content);
            ActiveTerminal = nextActive;
            nextActive?.Focus();
        }
    }

    /// <summary>Creates a new tab in the multiplexer.</summary>
    public TerminalTabItem AddTab(string? title = null)
    {
        var term = CreateTerminal();
        var tab = _tabControl.AddTab(title, term);
        ActiveTerminal = term;
        term.Focus();
        return tab;
    }

    private TerminalControl CreateTerminal()
    {
        var terminal = TerminalControlFactory?.Invoke() ?? new TerminalControl();
        return terminal;
    }

    private void OnDescendantGotFocus(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep)
        {
            var tc = FindParentTerminal(dep);
            if (tc != null)
                ActiveTerminal = tc;
        }
    }

    private static TerminalControl? FindParentTerminal(DependencyObject? current)
    {
        while (current != null)
        {
            if (current is TerminalControl tc)
                return tc;

            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static TerminalControl? FindFirstTerminal(UIElement? element)
    {
        if (element is TerminalControl tc)
            return tc;

        if (element is TerminalSplitControl sc)
            return sc.FindActiveTerminal();

        return null;
    }

    private static Button CreateActionButton(string icon, string tooltip, RoutedEventHandler onClick)
    {
        var btn = new Button
        {
            Content = icon,
            Width = 26,
            Height = 24,
            Margin = new Thickness(2, 0, 2, 0),
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            ToolTip = tooltip,
        };
        btn.Click += onClick;
        return btn;
    }
}

using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace VirtualTerminal.Controls;

/// <summary>
/// Represents a single tab within a <see cref="TerminalTabControl"/> in Avalonia.
/// </summary>
public class TerminalTabItem
{
    /// <summary>Gets or sets the display title of the tab.</summary>
    public string Title { get; set; } = "Terminal";

    /// <summary>Gets or sets the content element (such as a <see cref="TerminalControl"/> or <see cref="TerminalSplitControl"/>).</summary>
    public Control? Content { get; set; }

    /// <summary>Gets or sets custom user data associated with the tab.</summary>
    public object? Tag { get; set; }
}

/// <summary>
/// Tabbed container managing multiple terminal sessions with interactive add (+) and close (×) buttons in Avalonia.
/// </summary>
public class TerminalTabControl : Grid
{
    private readonly StackPanel _tabHeadersPanel;
    private readonly ContentPresenter _contentArea;
    private readonly Button _addTabButton;
    private readonly ObservableCollection<TerminalTabItem> _tabs = new();
    private TerminalTabItem? _selectedTab;

    /// <summary>Event raised when the active selected tab changes.</summary>
    public event EventHandler<TerminalTabItem?>? SelectedTabChanged;

    /// <summary>Event raised when a tab is closed.</summary>
    public event EventHandler<TerminalTabItem>? TabClosed;

    /// <summary>Event raised when a new tab is added.</summary>
    public event EventHandler<TerminalTabItem>? TabAdded;

    /// <summary>Factory method invoked when the user clicks the '+' button to generate new tab content.</summary>
    public Func<Control>? NewTabContentFactory { get; set; }

    /// <summary>Gets the collection of tabs.</summary>
    public ReadOnlyObservableCollection<TerminalTabItem> Tabs { get; }

    /// <summary>Initializes a new instance of <see cref="TerminalTabControl"/>.</summary>
    public TerminalTabControl()
    {
        Tabs = new ReadOnlyObservableCollection<TerminalTabItem>(_tabs);

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Tab strip header container
        var headerBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(37, 37, 38)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            MinHeight = 32,
        };

        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        _tabHeadersPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
        };
        scrollViewer.Content = _tabHeadersPanel;
        SetColumn(scrollViewer, 0);
        headerGrid.Children.Add(scrollViewer);

        _addTabButton = new Button
        {
            // button props
            Content = "+",
            Width = 28,
            Height = 26,
            Margin = new Thickness(4, 3, 4, 3),
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(204, 204, 204)),
            BorderThickness = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            
        };
        _addTabButton.Click += (s, e) => AddNewTab();
        ToolTip.SetTip(_addTabButton, "New Terminal");
        SetColumn(_addTabButton, 1);
        headerGrid.Children.Add(_addTabButton);

        headerBorder.Child = headerGrid;
        SetRow(headerBorder, 0);
        Children.Add(headerBorder);

        // Content area
        _contentArea = new ContentPresenter
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        SetRow(_contentArea, 1);
        Children.Add(_contentArea);
    }

    /// <summary>Gets or sets the currently selected tab.</summary>
    public TerminalTabItem? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (!ReferenceEquals(_selectedTab, value))
            {
                _selectedTab = value;
                _contentArea.Content = value?.Content;
                UpdateTabHeaderVisuals();
                SelectedTabChanged?.Invoke(this, value);
            }
        }
    }

    /// <summary>Adds a new tab with optional title and content.</summary>
    public TerminalTabItem AddTab(string? title = null, Control? content = null)
    {
        content ??= NewTabContentFactory?.Invoke() ?? new TerminalControl();
        title ??= $"Terminal {_tabs.Count + 1}";

        var item = new TerminalTabItem
        {
            Title = title,
            Content = content,
        };

        _tabs.Add(item);
        RebuildTabHeaders();
        SelectedTab = item;
        TabAdded?.Invoke(this, item);
        return item;
    }

    /// <summary>Closes the specified tab.</summary>
    public void CloseTab(TerminalTabItem tab)
    {
        int index = _tabs.IndexOf(tab);
        if (index < 0)
            return;

        _tabs.RemoveAt(index);

        if (ReferenceEquals(_selectedTab, tab))
        {
            if (_tabs.Count > 0)
            {
                int newIndex = Math.Clamp(index, 0, _tabs.Count - 1);
                SelectedTab = _tabs[newIndex];
            }
            else
            {
                SelectedTab = null;
            }
        }

        RebuildTabHeaders();
        TabClosed?.Invoke(this, tab);
    }

    private void AddNewTab()
    {
        AddTab();
    }

    private void RebuildTabHeaders()
    {
        _tabHeadersPanel.Children.Clear();

        foreach (var tab in _tabs)
        {
            var header = CreateTabHeaderControl(tab);
            _tabHeadersPanel.Children.Add(header);
        }

        UpdateTabHeaderVisuals();
    }

    private Control CreateTabHeaderControl(TerminalTabItem tab)
    {
        var border = new Border
        {
            Tag = tab,
            Padding = new Thickness(10, 4, 6, 4),
            Margin = new Thickness(0, 0, 1, 0),
            Cursor = new Cursor(StandardCursorType.Hand),
            CornerRadius = new CornerRadius(3, 3, 0, 0),
        };

        var sp = new StackPanel { Orientation = Orientation.Horizontal };

        var titleText = new TextBlock
        {
            Text = tab.Title,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
            Margin = new Thickness(0, 0, 8, 0),
        };
        sp.Children.Add(titleText);

        var closeButton = new Button
        {
            Content = "×",
            Width = 18,
            Height = 18,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
            BorderThickness = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
            FontSize = 14,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            
        };
        ToolTip.SetTip(closeButton, "Close Tab");
        closeButton.Click += (s, e) =>
        {
            e.Handled = true;
            CloseTab(tab);
        };
        sp.Children.Add(closeButton);

        border.Child = sp;
        border.PointerPressed += (s, e) =>
        {
            var point = e.GetCurrentPoint(border);
            if (point.Properties.IsLeftButtonPressed)
            {
                SelectedTab = tab;
            }
            else if (point.Properties.IsMiddleButtonPressed)
            {
                CloseTab(tab);
            }
        };

        return border;
    }

    private void UpdateTabHeaderVisuals()
    {
        foreach (Control child in _tabHeadersPanel.Children)
        {
            if (child is Border border && border.Tag is TerminalTabItem tab)
            {
                bool isSelected = ReferenceEquals(_selectedTab, tab);
                border.Background = new SolidColorBrush(isSelected
                    ? Color.FromRgb(30, 30, 30)
                    : Color.FromRgb(45, 45, 48));

                if (border.Child is StackPanel sp && sp.Children[0] is TextBlock tb)
                {
                    tb.Foreground = new SolidColorBrush(isSelected
                        ? Color.FromRgb(255, 255, 255)
                        : Color.FromRgb(160, 160, 160));
                    tb.FontWeight = isSelected ? FontWeight.SemiBold : FontWeight.Normal;
                }
            }
        }
    }
}

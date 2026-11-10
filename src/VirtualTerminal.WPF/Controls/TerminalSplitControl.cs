using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VirtualTerminal.Controls;

/// <summary>
/// Container that splits two terminal or child panes horizontally or vertically using an interactive <see cref="GridSplitter"/>.
/// Supports arbitrary nested split trees.
/// </summary>
public class TerminalSplitControl : Grid
{
    private readonly GridSplitter _splitter;
    private UIElement? _pane1;
    private UIElement? _pane2;
    private Orientation _orientation;
    private double _splitRatio = 0.5;
    private double _splitterThickness = 4.0;

    /// <summary>
    /// Event raised when a pane inside this split container requests to be closed.
    /// </summary>
    public event EventHandler<UIElement>? PaneCloseRequested;

    /// <summary>Initializes a new empty <see cref="TerminalSplitControl"/>.</summary>
    public TerminalSplitControl() : this(null, null, Orientation.Vertical)
    {
    }

    /// <summary>Initializes a new <see cref="TerminalSplitControl"/> with two child elements and orientation.</summary>
    public TerminalSplitControl(UIElement? pane1, UIElement? pane2, Orientation orientation = Orientation.Vertical)
    {
        _orientation = orientation;
        _splitter = new GridSplitter
        {
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ShowsPreview = false,
        };

        SetPanes(pane1, pane2);
    }

    /// <summary>Gets or sets the first pane in the split.</summary>
    public UIElement? Pane1
    {
        get => _pane1;
        set => SetPanes(value, _pane2);
    }

    /// <summary>Gets or sets the second pane in the split.</summary>
    public UIElement? Pane2
    {
        get => _pane2;
        set => SetPanes(_pane1, value);
    }

    /// <summary>Gets or sets the split orientation (Vertical = left/right panes, Horizontal = top/bottom panes).</summary>
    public Orientation Orientation
    {
        get => _orientation;
        set
        {
            if (_orientation != value)
            {
                _orientation = value;
                RebuildLayout();
            }
        }
    }

    /// <summary>Gets or sets the initial split ratio between Pane1 and Pane2 (from 0.0 to 1.0, default 0.5).</summary>
    public double SplitRatio
    {
        get => _splitRatio;
        set
        {
            _splitRatio = Math.Clamp(value, 0.05, 0.95);
            RebuildLayout();
        }
    }

    /// <summary>Gets or sets the thickness of the splitter drag handle.</summary>
    public double SplitterThickness
    {
        get => _splitterThickness;
        set
        {
            _splitterThickness = Math.Max(1.0, value);
            RebuildLayout();
        }
    }

    /// <summary>Sets both panes simultaneously and updates layout.</summary>
    public void SetPanes(UIElement? pane1, UIElement? pane2)
    {
        _pane1 = pane1;
        _pane2 = pane2;
        RebuildLayout();
    }

    /// <summary>
    /// Replaces an existing child element in this split control with a new element.
    /// </summary>
    public bool ReplaceChild(UIElement oldChild, UIElement newChild)
    {
        if (ReferenceEquals(_pane1, oldChild))
        {
            SetPanes(newChild, _pane2);
            return true;
        }

        if (ReferenceEquals(_pane2, oldChild))
        {
            SetPanes(_pane1, newChild);
            return true;
        }

        if (_pane1 is TerminalSplitControl childSplit1 && childSplit1.ReplaceChild(oldChild, newChild))
            return true;

        if (_pane2 is TerminalSplitControl childSplit2 && childSplit2.ReplaceChild(oldChild, newChild))
            return true;

        return false;
    }

    /// <summary>
    /// Closes the specified child pane and returns the surviving sibling pane.
    /// </summary>
    public UIElement? CloseChild(UIElement childToClose)
    {
        PaneCloseRequested?.Invoke(this, childToClose);
        if (ReferenceEquals(_pane1, childToClose))
            return _pane2;

        if (ReferenceEquals(_pane2, childToClose))
            return _pane1;

        if (_pane1 is TerminalSplitControl childSplit1)
        {
            UIElement? remaining = childSplit1.CloseChild(childToClose);
            if (remaining != null && !ReferenceEquals(remaining, _pane1))
            {
                ReplaceChild(_pane1, remaining);
                return this;
            }
        }

        if (_pane2 is TerminalSplitControl childSplit2)
        {
            UIElement? remaining = childSplit2.CloseChild(childToClose);
            if (remaining != null && !ReferenceEquals(remaining, _pane2))
            {
                ReplaceChild(_pane2, remaining);
                return this;
            }
        }

        return this;
    }

    /// <summary>
    /// Finds the currently focused <see cref="TerminalControl"/> or the first <see cref="TerminalControl"/> in this subtree.
    /// </summary>
    public TerminalControl? FindActiveTerminal()
    {
        TerminalControl? firstFound = null;

        TerminalControl? Search(UIElement? element)
        {
            if (element is TerminalControl tc)
            {
                firstFound ??= tc;
                if (tc.IsFocused)
                    return tc;
            }
            else if (element is TerminalSplitControl sc)
            {
                var active = Search(sc._pane1);
                if (active != null)
                    return active;
                active = Search(sc._pane2);
                if (active != null)
                    return active;
            }
            return null;
        }

        return Search(this) ?? firstFound;
    }

    private void RebuildLayout()
    {
        Children.Clear();
        ColumnDefinitions.Clear();
        RowDefinitions.Clear();

        if (_pane1 == null && _pane2 == null)
            return;

        if (_pane1 != null && _pane2 == null)
        {
            Children.Add(_pane1);
            SetRow(_pane1, 0);
            SetColumn(_pane1, 0);
            return;
        }

        if (_pane1 == null && _pane2 != null)
        {
            Children.Add(_pane2);
            SetRow(_pane2, 0);
            SetColumn(_pane2, 0);
            return;
        }

        // Both panes are non-null: create split with GridSplitter
        if (_orientation == Orientation.Vertical)
        {
            // Left/right split
            _splitter.ResizeDirection = GridResizeDirection.Columns;
            _splitter.Width = _splitterThickness;
            _splitter.Height = double.NaN;

            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_splitRatio, GridUnitType.Star) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0 - _splitRatio, GridUnitType.Star) });

            SetColumn(_pane1!, 0);
            SetRow(_pane1!, 0);
            Children.Add(_pane1!);

            SetColumn(_splitter, 1);
            SetRow(_splitter, 0);
            Children.Add(_splitter);

            SetColumn(_pane2!, 2);
            SetRow(_pane2!, 0);
            Children.Add(_pane2!);
        }
        else
        {
            // Top/bottom split
            _splitter.ResizeDirection = GridResizeDirection.Rows;
            _splitter.Height = _splitterThickness;
            _splitter.Width = double.NaN;

            RowDefinitions.Add(new RowDefinition { Height = new GridLength(_splitRatio, GridUnitType.Star) });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0 - _splitRatio, GridUnitType.Star) });

            SetRow(_pane1!, 0);
            SetColumn(_pane1!, 0);
            Children.Add(_pane1!);

            SetRow(_splitter, 1);
            SetColumn(_splitter, 0);
            Children.Add(_splitter);

            SetRow(_pane2!, 2);
            SetColumn(_pane2!, 0);
            Children.Add(_pane2!);
        }
    }
}

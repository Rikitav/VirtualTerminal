using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Automation.Text;

namespace VirtualTerminal.Automation;

/// <summary>
/// UI Automation peer for the WPF <see cref="TerminalControl"/>. Exposes the terminal as a
/// document and implements <see cref="ITextProvider"/> so screen readers can access the full
/// scrollback + visible buffer text.
/// </summary>
internal sealed class TerminalControlAutomationPeer : FrameworkElementAutomationPeer, ITextProvider
{
    private string _text = string.Empty;

    public TerminalControlAutomationPeer(TerminalControl owner)
        : base(owner)
    {
        UpdateText();
    }

    private TerminalControl TerminalControl => (TerminalControl)Owner;

    /// <inheritdoc />
    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Document;

    /// <inheritdoc />
    protected override string GetClassNameCore()
        => nameof(TerminalControl);

    /// <inheritdoc />
    protected override string GetNameCore()
    {
        string? title = TerminalControl.Session?.Title;
        return string.IsNullOrEmpty(title) ? "Terminal" : $"Terminal - {title}";
    }

    /// <inheritdoc />
    public override object? GetPattern(PatternInterface patternInterface)
    {
        if (patternInterface == PatternInterface.Text)
            return this;

        return base.GetPattern(patternInterface);
    }

    /// <inheritdoc />
    protected override string GetAutomationIdCore()
        => "TerminalControl";

    /// <inheritdoc />
    protected override bool IsContentElementCore()
        => true;

    /// <inheritdoc />
    protected override bool IsControlElementCore()
        => true;

    /// <inheritdoc />
    protected override bool IsKeyboardFocusableCore()
        => true;

    // ---- ITextProvider ----

    /// <inheritdoc />
    public ITextRangeProvider DocumentRange
        => new TerminalTextRangeProvider(this, _text, 0, _text.Length);

    /// <inheritdoc />
    public SupportedTextSelection SupportedTextSelection
        => SupportedTextSelection.None;

    /// <inheritdoc />
    public ITextRangeProvider[] GetSelection()
        => [];

    /// <inheritdoc />
    public ITextRangeProvider[] GetVisibleRanges()
        => [DocumentRange];

    /// <inheritdoc />
    public ITextRangeProvider? RangeFromChild(IRawElementProviderSimple childElement)
        => null;

    /// <inheritdoc />
    public ITextRangeProvider RangeFromPoint(Point screenLocation)
        => DocumentRange;

    /// <summary>
    /// Refreshes the cached accessible text from the control and raises the appropriate
    /// automation events when it changes. Must be called on the UI thread.
    /// </summary>
    internal void UpdateText()
    {
        string newText = TerminalControl.GetAccessibleText();
        if (_text == newText)
            return;

        string oldText = _text;
        _text = newText;

        RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, oldText, newText);
        AutomationInteropProvider.RaiseAutomationEvent(
            TextPattern.TextChangedEvent,
            ProviderFromPeer(this),
            new AutomationEventArgs(TextPattern.TextChangedEvent));
    }

    /// <summary>
    /// Returns the raw UI Automation provider for this peer so text ranges can reference it.
    /// </summary>
    internal IRawElementProviderSimple GetRawProvider()
        => ProviderFromPeer(this);
}

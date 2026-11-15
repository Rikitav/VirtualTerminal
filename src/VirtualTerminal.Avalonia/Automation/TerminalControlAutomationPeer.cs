using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Threading;

namespace VirtualTerminal.Automation;

/// <summary>
/// Automation peer for the Avalonia <see cref="TerminalControl"/>.
/// Avalonia 12.0.5 does not ship a cross-platform <c>ITextProvider</c>, so the terminal text is
/// exposed through <see cref="IValueProvider"/> and the element's live Name property.
/// </summary>
internal sealed class TerminalControlAutomationPeer : ControlAutomationPeer, IValueProvider
{
    private const int MaxNameLength = 512;

    public TerminalControlAutomationPeer(TerminalControl owner)
        : base(owner)
    {
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
        string text = TerminalControl.GetAccessibleText();
        if (string.IsNullOrEmpty(text))
            return "Terminal";

        string preview = text.Length <= MaxNameLength ? text : text[..MaxNameLength] + "…";
        return $"Terminal: {preview}";
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

    /// <inheritdoc />
    protected override object? GetProviderCore(System.Type patternInterface)
    {
        if (patternInterface == typeof(IValueProvider))
            return this;

        return base.GetProviderCore(patternInterface);
    }

    // ---- IValueProvider ----

    /// <inheritdoc />
    public string Value => TerminalControl.GetAccessibleText();

    /// <inheritdoc />
    public bool IsReadOnly => true;

    /// <inheritdoc />
    public void SetValue(string? value)
    {
    }

    /// <summary>
    /// Notifies assistive technologies that the terminal text has changed.
    /// </summary>
    internal void NotifyTextChanged()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(NotifyTextChanged);
            return;
        }

        string newValue = Value;
        RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, null, newValue);
        RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, null, GetNameCore());
    }
}

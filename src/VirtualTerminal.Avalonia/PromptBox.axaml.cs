using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using System.Text;
using VirtualTerminal.Buffer;
using VirtualTerminal.Extensions;
using VirtualTerminal.Input;
using VirtualTerminal.Interfaces;

namespace VirtualTerminal;

/// <summary>
/// Routed event arguments for <see cref="PromptBox.CommandSubmitted"/>, containing the submitted command text.
/// </summary>
public class CommandSubmittedEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Gets the submitted command text.
    /// </summary>
    public string CommandText { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandSubmittedEventArgs"/> class.
    /// </summary>
    public CommandSubmittedEventArgs(RoutedEvent routedEvent, object? source, string commandText)
        : base(routedEvent, source)
    {
        CommandText = commandText;
    }
}

/// <summary>
/// Routed event arguments for <see cref="PromptBox.KeyPressed"/>, containing raw key bytes sent to the session.
/// </summary>
public class KeyPressedEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Gets the raw key data that was sent to the terminal session.
    /// </summary>
    public byte[] KeyData { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="KeyPressedEventArgs"/> class.
    /// </summary>
    public KeyPressedEventArgs(RoutedEvent routedEvent, object? source, byte[] keyData)
        : base(routedEvent, source)
    {
        KeyData = keyData;
    }
}

/// <summary>
/// A small command entry control (prompt + input + submit button) that can optionally send submitted commands
/// into an attached <see cref="ITerminalSession"/>.
/// </summary>
public partial class PromptBox : UserControl
{
    /// <summary>
    /// Defines the <see cref="Session"/> styled property.
    /// </summary>
    public static readonly StyledProperty<ITerminalSession?> SessionProperty =
        AvaloniaProperty.Register<PromptBox, ITerminalSession?>(nameof(Session), null);

    /// <summary>
    /// Defines the <see cref="InputText"/> styled property.
    /// </summary>
    public static readonly StyledProperty<string> InputTextProperty =
        AvaloniaProperty.Register<PromptBox, string>(nameof(InputText), string.Empty);

    /// <summary>
    /// Defines the <see cref="IsInputEnabled"/> styled property.
    /// </summary>
    public static readonly StyledProperty<bool> IsInputEnabledProperty =
        AvaloniaProperty.Register<PromptBox, bool>(nameof(IsInputEnabled), true);

    /// <summary>
    /// Defines the <see cref="Prompt"/> styled property.
    /// </summary>
    public static readonly StyledProperty<string> PromptProperty =
        AvaloniaProperty.Register<PromptBox, string>(nameof(Prompt), "> ");

    /// <summary>
    /// Defines the <see cref="CommandSubmitted"/> routed event.
    /// </summary>
    public static readonly RoutedEvent<CommandSubmittedEventArgs> CommandSubmittedEvent =
        RoutedEvent.Register<PromptBox, CommandSubmittedEventArgs>(nameof(CommandSubmitted), RoutingStrategies.Bubble);

    /// <summary>
    /// Defines the <see cref="KeyPressed"/> routed event.
    /// </summary>
    public static readonly RoutedEvent<KeyPressedEventArgs> KeyPressedEvent =
        RoutedEvent.Register<PromptBox, KeyPressedEventArgs>(nameof(KeyPressed), RoutingStrategies.Bubble);

    /// <summary>
    /// Gets or sets the target <see cref="ITerminalSession"/>. If set, submitted commands and special keys
    /// are sent to the session.
    /// </summary>
    public ITerminalSession? Session
    {
        get => GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    /// <summary>
    /// Gets or sets the current input text.
    /// </summary>
    public string InputText
    {
        get => GetValue(InputTextProperty);
        set => SetValue(InputTextProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the input box is enabled.
    /// </summary>
    public bool IsInputEnabled
    {
        get => GetValue(IsInputEnabledProperty);
        set => SetValue(IsInputEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets the prompt label text displayed before the input box (for example, <c>&gt; </c>).
    /// </summary>
    public string Prompt
    {
        get => GetValue(PromptProperty);
        set => SetValue(PromptProperty, value);
    }

    /// <summary>
    /// Occurs when a special key (VT sequence) is pressed and forwarded. Routed event (bubbling).
    /// </summary>
    public event EventHandler<CommandSubmittedEventArgs>? CommandSubmitted
    {
        add => AddHandler(CommandSubmittedEvent, value);
        remove => RemoveHandler(CommandSubmittedEvent, value);
    }

    /// <summary>
    /// Occurs when the user submits a command (Enter or submit button). Routed event (bubbling).
    /// </summary>
    public event EventHandler<KeyPressedEventArgs>? KeyPressed
    {
        add => AddHandler(KeyPressedEvent, value);
        remove => RemoveHandler(KeyPressedEvent, value);
    }

    /// <summary>
    /// Initializes a new <see cref="PromptBox"/>.
    /// </summary>
    public PromptBox()
    {
        InitializeComponent();

        if (PART_Input is not null)
        {
            PART_Input.AddHandler(
                InputElement.KeyDownEvent,
                OnInputPreviewKeyDown,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
        }
    }

    private void OnInputPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (IsModifier(e.Key))
            return;

        if (e.Key == Key.Enter)
        {
            SubmitCommand();
            e.Handled = true;
            return;
        }

        // Let Backspace and printable characters edit the local input box.
        if (e.Key == Key.Back)
            return;

        TerminalKey terminalKey = ToTerminalKey(e.Key);
        if (terminalKey == TerminalKey.None)
            return;

        e.Handled = true;
        SendKey(terminalKey, ToTerminalModifier(e.KeyModifiers));
    }

    private void PART_Enter_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        SubmitCommand();
    }

    private void PART_Input_GotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (PART_Input is not null)
            PART_Input.CaretIndex = PART_Input.Text?.Length ?? 0;
    }

    private void SubmitCommand()
    {
        string command = InputText ?? string.Empty;
        InputText = string.Empty;

        CommandSubmittedEventArgs args = new(CommandSubmittedEvent, this, command);
        RaiseEvent(args);

        if (Session is null)
            return;

        Session.AppendLine(command);
    }

    private void SendKey(TerminalKey key, TerminalModifier modifiers)
    {
        if (Session is null)
            return;

        TerminalModes modes = (Session.Decoder as TerminalDecoder)?.State.Modes ?? new TerminalModes();
        string? seq = KeyboardEncoder.Encode(key, modifiers, in modes);
        if (string.IsNullOrEmpty(seq))
            return;

        byte[] data = Session.InputEncoding.GetBytes(seq);
        KeyPressedEventArgs args = new(KeyPressedEvent, this, data);
        RaiseEvent(args);

        Session.Append(seq);
    }

    private static bool IsModifier(Key key)
        => key == Key.LeftShift || key == Key.RightShift
        || key == Key.LeftCtrl || key == Key.RightCtrl
        || key == Key.LeftAlt || key == Key.RightAlt;

    private static TerminalKey ToTerminalKey(Key key) => key switch
    {
        Key.Enter => TerminalKey.Enter,
        Key.Back => TerminalKey.Back,
        Key.Tab => TerminalKey.Tab,
        Key.Escape => TerminalKey.Escape,
        Key.Up => TerminalKey.Up,
        Key.Down => TerminalKey.Down,
        Key.Right => TerminalKey.Right,
        Key.Left => TerminalKey.Left,
        Key.Home => TerminalKey.Home,
        Key.Insert => TerminalKey.Insert,
        Key.Delete => TerminalKey.Delete,
        Key.End => TerminalKey.End,
        Key.PageUp => TerminalKey.PageUp,
        Key.PageDown => TerminalKey.PageDown,
        Key.F1 => TerminalKey.F1,
        Key.F2 => TerminalKey.F2,
        Key.F3 => TerminalKey.F3,
        Key.F4 => TerminalKey.F4,
        Key.F5 => TerminalKey.F5,
        Key.F6 => TerminalKey.F6,
        Key.F7 => TerminalKey.F7,
        Key.F8 => TerminalKey.F8,
        Key.F9 => TerminalKey.F9,
        Key.F10 => TerminalKey.F10,
        Key.F11 => TerminalKey.F11,
        Key.F12 => TerminalKey.F12,
        _ => TerminalKey.None,
    };

    private static TerminalModifier ToTerminalModifier(KeyModifiers modifiers)
    {
        TerminalModifier result = TerminalModifier.None;
        if ((modifiers & KeyModifiers.Shift) != 0)
            result |= TerminalModifier.Shift;
        if ((modifiers & KeyModifiers.Alt) != 0)
            result |= TerminalModifier.Alt;
        if ((modifiers & KeyModifiers.Control) != 0)
            result |= TerminalModifier.Control;
        if ((modifiers & KeyModifiers.Meta) != 0)
            result |= TerminalModifier.Meta;
        return result;
    }
}

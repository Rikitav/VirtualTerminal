using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VirtualTerminal.Buffer;
using VirtualTerminal.Extensions;
using VirtualTerminal.Input;
using VirtualTerminal.Interfaces;

namespace VirtualTerminal;

/// <summary>
/// Routed event handler for <see cref="PromptBox.CommandSubmitted"/>. Raised when the user submits a command
/// (Enter key or clicking the submit button).
/// </summary>
/// <param name="sender">The object that raised the event.</param>
/// <param name="e">Event data containing the submitted command.</param>
public delegate void CommandSubmittedEventHandler(object sender, CommandSubmittedEventArgs e);

/// <summary>
/// Routed event handler for <see cref="PromptBox.KeyPressed"/>. Raised when a raw key sequence is forwarded
/// to the attached terminal session.
/// </summary>
/// <param name="sender">The object that raised the event.</param>
/// <param name="e">Event data containing the raw key bytes.</param>
public delegate void KeyPressedEventHandler(object sender, KeyPressedEventArgs e);

/// <summary>
/// Routed event arguments for <see cref="PromptBox.CommandSubmitted"/>, containing the submitted command text.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="CommandSubmittedEventArgs"/> class.
/// </remarks>
public class CommandSubmittedEventArgs(RoutedEvent routedEvent, object? source, string commandText) : RoutedEventArgs(routedEvent, source)
{
    /// <summary>
    /// Gets the submitted command text.
    /// </summary>
    public string CommandText { get; } = commandText;
}

/// <summary>
/// Routed event arguments for <see cref="PromptBox.KeyPressed"/>, containing raw key bytes sent to the session.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="KeyPressedEventArgs"/> class.
/// </remarks>
public class KeyPressedEventArgs(RoutedEvent routedEvent, object? source, byte[] keyData) : RoutedEventArgs(routedEvent, source)
{
    /// <summary>
    /// Gets the raw key data that was sent to the terminal session.
    /// </summary>
    public byte[] KeyData { get; } = keyData;
}

/// <summary>
/// A small command entry control (prompt + input + submit button) that can optionally send submitted commands
/// into an attached <see cref="ITerminalSession"/>.
/// </summary>
public partial class PromptBox : UserControl
{
    /// <summary>
    /// Gets or sets the target <see cref="ITerminalSession"/>. If set, submitted commands and special keys
    /// are sent to the session.
    /// </summary>
    public ITerminalSession? Session
    {
        get => (ITerminalSession?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    /// <summary>
    /// Gets or sets the current input text.
    /// </summary>
    public string InputText
    {
        get => (string)GetValue(InputTextProperty);
        set => SetValue(InputTextProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the input box is enabled.
    /// </summary>
    public bool IsInputEnabled
    {
        get => (bool)GetValue(IsInputEnabledProperty);
        set => SetValue(IsInputEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets the prompt label text displayed before the input box (for example, <c>&gt; </c>).
    /// </summary>
    public string Prompt
    {
        get => (string)GetValue(PromptProperty);
        set => SetValue(PromptProperty, value);
    }

    /// <summary>
    /// Occurs when a special key (VT sequence) is pressed and forwarded. Routed event (bubbling).
    /// </summary>
    public event KeyPressedEventHandler KeyPressed
    {
        add => AddHandler(KeyPressedEvent, value);
        remove => RemoveHandler(KeyPressedEvent, value);
    }

    /// <summary>
    /// Occurs when the user submits a command (Enter or submit button). Routed event (bubbling).
    /// </summary>
    public event CommandSubmittedEventHandler CommandSubmitted
    {
        add => AddHandler(CommandSubmittedEvent, value);
        remove => RemoveHandler(CommandSubmittedEvent, value);
    }

    /// <summary>
    /// Initializes a new <see cref="PromptBox"/>.
    /// </summary>
    public PromptBox()
    {
        Foreground = Brushes.White;
        InitializeComponent();
    }

    private void PART_Input_PreviewKeyDown(object sender, KeyEventArgs e)
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
        SendKey(terminalKey, ToTerminalModifier(Keyboard.Modifiers));
    }

    private void PART_Enter_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        SubmitCommand();
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

    private void PART_Input_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
            textBox.CaretIndex = textBox.Text.Length;
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

    private static TerminalModifier ToTerminalModifier(ModifierKeys modifiers)
    {
        TerminalModifier result = TerminalModifier.None;
        if ((modifiers & ModifierKeys.Shift) != 0)
            result |= TerminalModifier.Shift;
        if ((modifiers & ModifierKeys.Alt) != 0)
            result |= TerminalModifier.Alt;
        if ((modifiers & ModifierKeys.Control) != 0)
            result |= TerminalModifier.Control;
        if ((modifiers & ModifierKeys.Windows) != 0)
            result |= TerminalModifier.Meta;
        return result;
    }

    /// <summary>
    /// Identifies the <see cref="Session"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty SessionProperty = DependencyProperty.Register(
        nameof(Session), typeof(ITerminalSession), typeof(PromptBox),
        new PropertyMetadata(defaultValue: null));

    /// <summary>
    /// Identifies the <see cref="InputText"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty InputTextProperty = DependencyProperty.Register(
        nameof(InputText), typeof(string), typeof(PromptBox),
        new PropertyMetadata(defaultValue: string.Empty));

    /// <summary>
    /// Identifies the <see cref="IsInputEnabled"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty IsInputEnabledProperty = DependencyProperty.Register(
        nameof(IsInputEnabled), typeof(bool), typeof(PromptBox),
        new PropertyMetadata(defaultValue: true));

    /// <summary>
    /// Identifies the <see cref="Prompt"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty PromptProperty = DependencyProperty.Register(
        nameof(Prompt), typeof(string), typeof(PromptBox),
        new PropertyMetadata(defaultValue: "> "));

    /// <summary>
    /// Identifies the <see cref="CommandSubmitted"/> routed event.
    /// </summary>
    public static readonly RoutedEvent CommandSubmittedEvent = EventManager.RegisterRoutedEvent(
        nameof(CommandSubmitted), RoutingStrategy.Bubble,
        typeof(CommandSubmittedEventHandler), typeof(PromptBox));

    /// <summary>
    /// Identifies the <see cref="KeyPressed"/> routed event.
    /// </summary>
    public static readonly RoutedEvent KeyPressedEvent = EventManager.RegisterRoutedEvent(
        nameof(KeyPressed), RoutingStrategy.Bubble,
        typeof(KeyPressedEventHandler), typeof(PromptBox));
}

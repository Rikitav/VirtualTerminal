using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VirtualTerminal.Interop;
using VirtualTerminal.Session;

namespace VirtualTerminal;

/// <summary>
/// Routed event handler for <see cref="PromptBox.CommandSubmitted"/>. Raised when the user submits a command
/// (Enter key or clicking the submit button).
/// </summary>
public delegate void CommandSubmittedEventHandler(object sender, CommandSubmittedEventArgs e);

/// <summary>
/// Routed event handler for <see cref="PromptBox.KeyPressed"/>. Intended for forwarding raw key input to listeners.
/// </summary>
public delegate void KeyPressedEventHandler(object sender, KeyPressedEventArgs e);

/// <summary>
/// Routed event arguments for <see cref="PromptBox.CommandSubmitted"/>, containing the submitted command text.
/// </summary>
public class CommandSubmittedEventArgs(RoutedEvent routedEvent, object source, string commandText) : RoutedEventArgs(routedEvent, source)
{
    /// <summary>
    /// Gets the submitted command text.
    /// </summary>
    public string CommandText { get; } = commandText;
}

/// <summary>
/// Routed event arguments for <see cref="PromptBox.KeyPressed"/>, containing raw key bytes.
/// </summary>
public class KeyPressedEventArgs(RoutedEvent routedEvent, object source, byte[] keyData) : RoutedEventArgs(routedEvent, source)
{
    /// <summary>
    /// Gets the raw key data that should be sent to a terminal session.
    /// </summary>
    public byte[] KeyData { get; } = keyData;
}

/// <summary>
/// A small command entry control (prompt + input + submit button) that can optionally send submitted commands
/// into an attached <see cref="TerminalSession"/>.
/// </summary>
public partial class PromptBox : UserControl
{
    /// <summary>
    /// Gets or sets the target <see cref="TerminalSession"/>. If set, submitted commands are sent to the session
    /// using <see cref="TerminalSessionExtensions.AppendLine(ITerminalSession,string)"/>.
    /// </summary>
    public TerminalSession? Session
    {
        get => (TerminalSession)GetValue(SessionProperty);
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
        if (KeyHelper.IsModifier(e.Key))
            return;

        if (e.Key == Key.Back)
            return;

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            string command = InputText ?? string.Empty;
            InputText = string.Empty;

            CommandSubmittedEventArgs args = new CommandSubmittedEventArgs(CommandSubmittedEvent, this, command);
            RaiseEvent(args);

            if (Session == null)
                return;

            Session.AppendLine(command);
            return;
        }

        Key actualKey = e.Key == Key.System ? e.SystemKey : e.Key;
        string? vtCode = KeyHelper.GetVT200Code(actualKey);
        
        if (vtCode != null)
        {
            /*
            if (Keyboard.Modifiers != ModifierKeys.Shift)
                return;
            */
            e.Handled = true;
            if (Session == null)
                return;

            Session.Append(vtCode);
            return;
        }

        /*
            PresentationSource? source = PresentationSource.FromVisual(this);
            if (source != null)
            {
                Key actualKey = e.Key == Key.System ? e.SystemKey : e.Key;
                KeyEventArgs args = new KeyEventArgs(Keyboard.PrimaryDevice, source, e.Timestamp, actualKey)
                {
                    RoutedEvent = KeyDownEvent
                };

                RaiseEvent(args);
                e.Handled = true;
            }
        */
    }

    private void PART_Enter_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        string command = InputText ?? string.Empty;
        InputText = string.Empty;

        CommandSubmittedEventArgs args = new CommandSubmittedEventArgs(CommandSubmittedEvent, this, command);
        RaiseEvent(args);

        if (Session == null)
            return;

        Session.AppendLine(command);
    }

    private void PART_Input_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.CaretIndex = textBox.Text.Length;
        }
    }

    /// <summary>
    /// Identifies the <see cref="Session"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty SessionProperty = DependencyProperty.Register(
        nameof(Session), typeof(TerminalSession), typeof(PromptBox),
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

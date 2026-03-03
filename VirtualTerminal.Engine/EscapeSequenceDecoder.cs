using System.Runtime.InteropServices;
using System.Text;
using VirtualTerminal.Engine.Components;

namespace VirtualTerminal.Engine;

public abstract class EscapeSequenceDecoder : IDecoder
{
    protected enum State
    {
        Ground,     // Обычный текст
        Escape,     // Получили \x1b
        CsiEntry,   // Получили [ после Escape
        CsiParam,   // Читаем цифры или ;

        OscEntry,   // Сразу после ]
        OscParam,   // Читаем номер команды (то самое '0')
        OscString,  // Читаем сам текст заголовка
        OscTermination // Если встретили ESC, проверяем на \ (для ST)
    }

    public const byte EscapeCharacter = 0x1B;
    public const byte LeftBracketCharacter = 0x5B;
    public const byte RightBracketCharacter = 0x5D;
    public const byte SemicolonCharacter = 0x3B;
    public const byte QuestionMarkCharacter = 0x3F;
    public const byte BelCharacter = 0x07;

    public const byte XonCharacter = 17;
    public const byte XoffCharacter = 19;

    private List<byte> oscPayload = [];
    private List<int> paramBuffer = [];
    private int paramAccumulator = 0;
    private bool hasParam = false;
    private bool privateMode = false;
    
    // Buffer for accumulating bytes for multi-byte character decoding
    private List<byte> characterBuffer = [];

    protected State state;
    protected bool supportXonXoff;
    protected bool xOffReceived;
    protected bool disposed;

    public Encoding Encoding
    {
        get => field;
        set => field = value;
    }

    public EscapeSequenceDecoder()
    {
        state = State.Ground;
        Encoding = Encoding.UTF8; // Use UTF-8 by default for proper Unicode support
        supportXonXoff = true;
        xOffReceived = false;
        characterBuffer = [];
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
            throw new ArgumentException("Input can not process an empty array.");

        foreach (byte b in data)
        {
            try
            {
                ProcessByte(b);
            }
            catch
            {
                _ = 0xBAD + 0xC0DE;
                Drain();
            }
        }
    }

    private void Drain()
    {
        FlushCharacterBuffer();
        state = State.Ground;
        paramAccumulator = 0;
        paramBuffer.Clear();
        privateMode = false;
        oscPayload.Clear();
    }

    private void ProcessByte(byte b)
    {
        switch (state)
        {
            case State.Ground:
                {
                    ProcessGround(b);
                    break;
                }

            case State.Escape:
                {
                    ProcessEscape(b);
                    break;
                }

            case State.CsiEntry:
                {
                    ProcessCsiEntry(b);
                    break;
                }

            case State.CsiParam:
                {
                    ProcessCsiParam(b);
                    break;
                }

            case State.OscEntry:
                {
                    ProcessOscEntry(b);
                    break;
                }

            case State.OscParam:
                {
                    ProcessOscParam(b);
                    break;
                }

            case State.OscString:
                {
                    ProcessOscString(b);
                    break;
                }

            case State.OscTermination:
                {
                    ProcessOscTermination(b);
                    break;
                }
        }
    }

    private void ProcessGround(byte b)
    {
        if (b == EscapeCharacter)
        {
            // Flush any accumulated bytes before processing escape sequence
            FlushCharacterBuffer();
            state = State.Escape;
            return;
        }

        // Accumulate bytes for multi-byte character decoding
        characterBuffer.Add(b);
        
        // Try to decode accumulated bytes
        TryDecodeCharacters();
    }
    
    private void TryDecodeCharacters()
    {
        if (characterBuffer.Count == 0)
            return;
            
        // Try to decode using the current encoding
        Decoder decoder = Encoding.GetDecoder();
        int maxCharCount = Encoding.GetMaxCharCount(characterBuffer.Count);
        char[] charBuffer = new char[maxCharCount];
        
        int bytesUsed, charsProduced;
        bool completed;
        
        decoder.Convert(characterBuffer.ToArray(), 0, characterBuffer.Count,
                       charBuffer, 0, maxCharCount, false,
                       out bytesUsed, out charsProduced, out completed);
        
        if (charsProduced > 0)
        {
            // Successfully decoded some characters
            OnCharacters(charBuffer.AsSpan(0, charsProduced));
            
            // Remove used bytes from buffer
            if (bytesUsed > 0)
            {
                characterBuffer.RemoveRange(0, bytesUsed);
            }
        }
        
        // If not completed and buffer is getting too large, flush what we can
        if (!completed && characterBuffer.Count > 4)
        {
            // Try to decode at least one character
            decoder.Convert(characterBuffer.ToArray(), 0, characterBuffer.Count,
                           charBuffer, 0, maxCharCount, true,
                           out bytesUsed, out charsProduced, out completed);
            
            if (charsProduced > 0)
            {
                OnCharacters(charBuffer.AsSpan(0, charsProduced));
                if (bytesUsed > 0)
                {
                    characterBuffer.RemoveRange(0, bytesUsed);
                }
            }
            else
            {
                // If we still can't decode, output as replacement character and clear buffer
                OnCharacters("\uFFFD".AsSpan());
                characterBuffer.Clear();
            }
        }
    }
    
    private void FlushCharacterBuffer()
    {
        if (characterBuffer.Count == 0)
            return;
            
        // Force decode remaining bytes
        Decoder decoder = Encoding.GetDecoder();
        int maxCharCount = Encoding.GetMaxCharCount(characterBuffer.Count);
        char[] charBuffer = new char[maxCharCount];
        
        int bytesUsed, charsProduced;
        bool completed;
        
        decoder.Convert(characterBuffer.ToArray(), 0, characterBuffer.Count,
                       charBuffer, 0, maxCharCount, true,
                       out bytesUsed, out charsProduced, out completed);
        
        if (charsProduced > 0)
        {
            OnCharacters(charBuffer.AsSpan(0, charsProduced));
        }
        else if (characterBuffer.Count > 0)
        {
            // If we can't decode, output replacement character
            OnCharacters("\uFFFD".AsSpan());
        }
        
        characterBuffer.Clear();
    }

    private void ProcessEscape(byte b)
    {
        if (b == LeftBracketCharacter)
        {
            state = State.CsiEntry;
            return;
        }

        if (b == RightBracketCharacter)
        {
            state = State.OscEntry;
            return;
        }

        // Обработка других последовательностей (например, ESC c - Reset)
        // Для упрощения возвращаемся в Ground
        state = State.Ground;
        return;
    }

    private void ProcessCsiEntry(byte b)
    {
        // Начало CSI. Здесь могут быть цифры, '?', '>', или сразу финал.
        if (IsDigit(b))
        {
            AccumulateParam(b);
            state = State.CsiParam;
            return;
        }
        
        if (b == SemicolonCharacter)
        {
            PopParam(b);
            state = State.CsiParam;
            return;
        }

        if (b == QuestionMarkCharacter)
        {
            privateMode = true;
            return;
        }

        if (IsCommand(b))
        {
            PopParam(b);
            DispatchCsi(b);
            return;
        }

        throw new InvalidByteException(b, "Unknown byte");
    }

    private void ProcessCsiParam(byte b)
    {
        if (IsDigit(b))
        {
            AccumulateParam(b);
            state = State.CsiParam;
            return;
        }

        if (b == SemicolonCharacter)
        {
            PopParam(b);
            state = State.CsiParam;
            return;
        }

        if (IsCommand(b))
        {
            PopParam(b);
            DispatchCsi(b);
            return;
        }

        throw new InvalidByteException(b, "Unknown byte");
    }

    private void ProcessOscEntry(byte b)
    {
        if (IsDigit(b))
        {
            AccumulateParam(b);
            state = State.OscParam;
            return;
        }

        if (b == SemicolonCharacter)
        {
            PopParam(b);
            state = State.OscParam;
            return;
        }

        if (b == QuestionMarkCharacter)
        {
            privateMode = true;
            return;
        }

        if (IsCommand(b))
        {
            PopParam(b);
            DispatchCsi(b);
            return;
        }

        throw new InvalidByteException(b, "Unknown byte");
    }

    private void ProcessOscParam(byte b)
    {
        if (IsDigit(b))
        {
            AccumulateParam(b);
            state = State.OscParam;
            return;
        }

        if (b == SemicolonCharacter)
        {
            PopParam(b);
            state = State.OscString;
            return;
        }

        if (b == BelCharacter)
        {
            DispatchOsc();
            return;
        }

        throw new InvalidByteException(b, "Unknown byte");
    }

    private void ProcessOscString(byte b)
    {
        if (b == BelCharacter) // <--- ВАРИАНТ 1: BEL (Звонок)
        {
            DispatchOsc(); // Выполняем команду!
            state = State.Ground;
            return;
        }
        
        if (b == EscapeCharacter) // <--- ВАРИАНТ 2: Начало ST (ESC \)
        {
            state = State.OscTermination;
            return;
        }

        // Накапливаем байты.
        oscPayload.Add(b);
    }

    private void ProcessOscTermination(byte b)
    {
        if (b == '\\') // Это был Backslash после ESC?
        {
            DispatchOsc(); // Выполняем!
            state = State.Ground;
            return;
        }

        // Это был не терминатор, а просто ESC внутри строки (редкость, но бывает)
        // Возвращаемся в парсинг строки
        //oscPayload.Append(EscapeCharacter);
        oscPayload.Add(b);
        state = State.OscString;
    }

    public void DispatchCsi(byte command)
    {
        ProcessCsiCommand(command, CollectionsMarshal.AsSpan(paramBuffer), privateMode);
        Drain();
    }

    public void DispatchOsc()
    {
        ProcessOscCommand(CollectionsMarshal.AsSpan(paramBuffer), Encoding.UTF8.GetString(CollectionsMarshal.AsSpan(oscPayload)));
        Drain();
    }

    protected abstract void OnCharacters(ReadOnlySpan<char> characters);
    protected abstract void ProcessCsiCommand(byte command, ReadOnlySpan<int> parameters, bool privateMode);
    protected abstract void ProcessOscCommand(ReadOnlySpan<int> parameters, string payload);

    protected void ThrowIsDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        Dispose(true);
        GC.SuppressFinalize(this);
        disposed = true;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        Drain();
    }

    private void AccumulateParam(byte b)
    {
        hasParam = true;
        paramAccumulator *= 10;
        paramAccumulator += b - 0x30;
    }

    private void PopParam(byte b)
    {
        if (!hasParam)
            return;

        paramBuffer.Add(paramAccumulator);
        paramAccumulator = 0;
        hasParam = false;
    }

    private static bool IsDigit(byte b)
    {
        return b >= 0x30 && b <= 0x39;
    }

    private static bool IsCommand(byte b)
    {
        return b >= 0x40 && b <= 0x7E;
    }
}

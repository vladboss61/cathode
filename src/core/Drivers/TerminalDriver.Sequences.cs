using static System.TerminalConstants;

namespace System.Drivers;

abstract partial class TerminalDriver
{
    // TODO: Decouple all of this from the driver.

    public TerminalMouseEvents MouseEvents { get; private set; }

    public string Title
    {
        get => _title;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            lock (_sequenceLock)
            {
                Sequence($"{OSC}0;{value}{BEL}");

                _title = value;
            }
        }
    }

    public TerminalKeyMode CursorKeyMode
    {
        get => _cursor;
        set
        {
            var type = value switch
            {
                TerminalKeyMode.Normal => 'l',
                TerminalKeyMode.Application => 'h',
                _ => throw new ArgumentOutOfRangeException(nameof(value)),
            };

            lock (_sequenceLock)
            {
                Sequence($"{CSI}?1{type}");

                _cursor = value;
            }
        }
    }

    public TerminalKeyMode NumericKeyMode
    {
        get => _numeric;
        set
        {
            var type = value switch
            {
                TerminalKeyMode.Normal => '>',
                TerminalKeyMode.Application => '=',
                _ => throw new ArgumentOutOfRangeException(nameof(value)),
            };

            lock (_sequenceLock)
            {
                Sequence($"{ESC}{type}");

                _numeric = value;
            }
        }
    }

    string _title = string.Empty;

    TerminalKeyMode _cursor;

    TerminalKeyMode _numeric;

    public void SetMouseEvents(TerminalMouseEvents events)
    {
        lock (_sequenceLock)
        {
            Sequence($"{CSI}?1003{(events.HasFlag(TerminalMouseEvents.Movement) ? 'h' : 'l')}");
            Sequence($"{CSI}?1006{(events.HasFlag(TerminalMouseEvents.Buttons) ? 'h' : 'l')}");

            MouseEvents = events;
        }
    }

    public void Sequence(string value)
    {
        if (!StdOut.IsRedirected)
            StdOut.Write(value);
        else if (!StdError.IsRedirected)
            StdError.Write(value);
    }

    public void Beep()
    {
        Sequence(BEL);
    }

    public void Insert(int count)
    {
        Sequence($"{CSI}{count}@");
    }

    public void Delete(int count)
    {
        Sequence($"{CSI}{count}P");
    }

    public void Erase(int count)
    {
        Sequence($"{CSI}{count}X");
    }

    public void InsertLine(int count)
    {
        Sequence($"{CSI}{count}L");
    }

    public void DeleteLine(int count)
    {
        Sequence($"{CSI}{count}M");
    }

    void Clear(char type, TerminalClearMode mode)
    {
        var m = mode switch
        {
            TerminalClearMode.Before => 1,
            TerminalClearMode.After => 0,
            TerminalClearMode.Full => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

        Sequence($"{CSI}{m}{type}");
    }

    public void ClearScreen(TerminalClearMode mode)
    {
        Clear('J', mode);
    }

    public void ClearLine(TerminalClearMode mode)
    {
        Clear('K', mode);
    }

    public void SetScrollRegion(int top, int bottom)
    {
        _ = top >= 0 ? true : throw new ArgumentOutOfRangeException(nameof(top));
        _ = bottom >= 0 ? true : throw new ArgumentOutOfRangeException(nameof(bottom));

        var size = Size;

        // Most terminals will clamp the values so that there are at least 2 lines of scrollable text: One for the last
        // line written and one for the current line. Enforce this. We throw TerminalException instead of
        // ArgumentException since the Size property could change between the caller observing it and calling this
        // method, so it would be somewhat inaccurate to call this condition a programmer error.
        if (size.Height - top - bottom < 2)
            throw new TerminalException("Scroll region is too small.");

        Sequence($"{CSI}{top + 1};{size.Height - bottom}r");
    }

    void MoveBuffer(char type, int count)
    {
        _ = count >= 0 ? true : throw new ArgumentOutOfRangeException(nameof(count));

        if (count != 0)
            Sequence($"{CSI}{count}{type}");
    }

    public void MoveBufferUp(int count)
    {
        MoveBuffer('S', count);
    }

    public void MoveBufferDown(int count)
    {
        MoveBuffer('T', count);
    }

    public void MoveCursorTo(int row, int column)
    {
        _ = row >= 0 ? true : throw new ArgumentOutOfRangeException(nameof(row));
        _ = column >= 0 ? true : throw new ArgumentOutOfRangeException(nameof(column));

        Sequence($"{CSI}{column + 1};{row + 1}H");
    }

    void MoveCursor(char type, int count)
    {
        _ = count >= 0 ? true : throw new ArgumentOutOfRangeException(nameof(count));

        if (count != 0)
            Sequence($"{CSI}{count}{type}");
    }

    public void MoveCursorUp(int count)
    {
        MoveCursor('A', count);
    }

    public void MoveCursorDown(int count)
    {
        MoveCursor('B', count);
    }

    public void MoveCursorLeft(int count)
    {
        MoveCursor('D', count);
    }

    public void MoveCursorRight(int count)
    {
        MoveCursor('C', count);
    }

    public void SaveCursorPosition()
    {
        Sequence($"{ESC}7");
    }

    public void RestoreCursorPosition()
    {
        Sequence($"{ESC}8");
    }

    public void ForegroundColor(byte r, byte g, byte b)
    {
        Sequence($"{CSI}38;2;{r};{g};{b}m");
    }

    public void BackgroundColor(byte r, byte g, byte b)
    {
        Sequence($"{CSI}48;2;{r};{g};{b}m");
    }

    public void Decorations(
        bool bold = false,
        bool faint = false,
        bool italic = false,
        bool underline = false,
        bool blink = false,
        bool invert = false,
        bool invisible = false,
        bool strike = false,
        bool doubleUnderline = false,
        bool overline = false)
    {
        Span<char> codes = stackalloc char[64];

        codes.Clear();

        var i = 0;

        void Handle(Span<char> result, ReadOnlySpan<char> code, bool value)
        {
            if (!value)
                return;

            if (i != 0)
                result[i++] = ';';

            code.CopyTo(result[i..code.Length]);

            i += code.Length;
        }

        Handle(codes, "1", bold);
        Handle(codes, "2", faint);
        Handle(codes, "3", italic);
        Handle(codes, "4", underline);
        Handle(codes, "5", blink);
        Handle(codes, "7", invert);
        Handle(codes, "8", invisible);
        Handle(codes, "9", strike);
        Handle(codes, "21", doubleUnderline);
        Handle(codes, "53", overline);

        Sequence($"{CSI}{codes.TrimEnd(char.MinValue).ToString()}m");
    }

    public void ResetAttributes()
    {
        Sequence($"{CSI}0m");
    }

    public void OpenHyperlink(Uri uri, string? id = null)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (id != null)
            id = $"id={id}";

        Sequence($"{OSC}8;{id};{uri}{BEL}");
    }

    public void CloseHyperlink()
    {
        Sequence($"{OSC}8;;{BEL}");
    }

    public void ResetAll()
    {
        Sequence($"{CSI}!p");
    }
}

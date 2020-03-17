using System.IO;
using System.Runtime.InteropServices;

namespace System
{
    public static class Terminal
    {
        const int ReaderBufferSize = 4096;

        public static ITerminalReader StdIn => _driver.StdIn;

        public static ITerminalWriter StdOut => _driver.StdOut;

        public static ITerminalWriter StdError => _driver.StdError;

        public static bool IsRawMode { get; private set; }

        public static int Width => _driver.Width switch
        {
            TerminalUtility.InvalidSize => throw new TerminalException("There is no terminal attached."),
            var width => width,
        };

        public static int Height => _driver.Height switch
        {
            TerminalUtility.InvalidSize => throw new TerminalException("There is no terminal attached."),
            var height => height,
        };

        public static string Title
        {
            get => _title;
            set
            {
                _ = value ?? throw new ArgumentNullException(nameof(value));

                lock (_titleLock)
                {
                    Sequence($"\x1b]0;{value}\a");

                    _title = value;
                }
            }
        }

        static readonly ITerminalDriver _driver = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ?
            (ITerminalDriver)new WindowsTerminalDriver() : new UnixTerminalDriver();

        static readonly BufferedStream _rawStream = new BufferedStream(StdIn.Stream, ReaderBufferSize);

        static readonly StreamReader _lineReader =
            new StreamReader(StdIn.Stream, StdIn.Encoding, false, ReaderBufferSize, true);

        static readonly object _titleLock = new object();

        static readonly object _readLock = new object();

        static string _title = string.Empty;

        static void Sequence(string value)
        {
            if (!StdOut.IsRedirected)
                Out(value);
            else if (!StdError.IsRedirected)
                Error(value);
        }

        public static void SetRawMode(bool raw, bool discard)
        {
            lock (_readLock)
            {
                _driver.SetRawMode(raw, discard);

                IsRawMode = raw;
            }
        }

        public static unsafe byte? ReadRaw()
        {
            lock (_readLock)
            {
                if (!IsRawMode)
                    throw new TerminalException("Terminal is not in raw mode.");

                byte value;

                return _rawStream.Read(new Span<byte>(&value, 1)) == 1 ? value : default;
            }
        }

        public static string? ReadLine()
        {
            lock (_readLock)
            {
                if (IsRawMode)
                    throw new TerminalException("Terminal is in raw mode.");

                return _lineReader.ReadLine();
            }
        }

        public static void OutBinary(ReadOnlySpan<byte> value)
        {
            _driver.StdOut.Write(value);
        }

        public static void OutText(ReadOnlySpan<char> value)
        {
            TerminalUtility.EncodeAndExecute(value, StdOut.Encoding, OutBinary);
        }

        public static void Out(string? value)
        {
            OutText(value.AsSpan());
        }

        public static void Out<T>(T value)
        {
            Out(value?.ToString());
        }

        public static void Out(string format, params object?[] args)
        {
            Out(string.Format(format, args));
        }

        public static void OutLine()
        {
            Out(Environment.NewLine);
        }

        public static void OutLine(string? value)
        {
            Out(value + Environment.NewLine);
        }

        public static void OutLine<T>(T value)
        {
            OutLine(value?.ToString());
        }

        public static void OutLine(string format, params object?[] args)
        {
            OutLine(string.Format(format, args));
        }

        public static void ErrorBinary(ReadOnlySpan<byte> value)
        {
            _driver.StdError.Write(value);
        }

        public static void ErrorText(ReadOnlySpan<char> value)
        {
            TerminalUtility.EncodeAndExecute(value, StdError.Encoding, ErrorBinary);
        }

        public static void Error(string? value)
        {
            ErrorText(value.AsSpan());
        }

        public static void Error<T>(T value)
        {
            Error(value?.ToString());
        }

        public static void Error(string format, params object?[] args)
        {
            Error(string.Format(format, args));
        }

        public static void ErrorLine()
        {
            Error(Environment.NewLine);
        }

        public static void ErrorLine(string? value)
        {
            Error(value + Environment.NewLine);
        }

        public static void ErrorLine<T>(T value)
        {
            ErrorLine(value?.ToString());
        }

        public static void ErrorLine(string format, params object?[] args)
        {
            ErrorLine(string.Format(format, args));
        }

        public static void Clear(bool cursor = true)
        {
            Sequence("\x1b[2J" + (cursor ? "\x1b[H" : string.Empty));
        }

        public static void Beep()
        {
            Sequence("\a");
        }
    }
}

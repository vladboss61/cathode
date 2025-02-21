namespace Vezel.Cathode.Terminals;

internal abstract class NativeTerminalWriter<TTerminal, THandle> : TerminalWriter
    where TTerminal : NativeVirtualTerminal<THandle>
{
    // Unlike NativeTerminalReader, the buffer size here is arbitrary and only has performance implications.
    private const int WriteBufferSize = 256;

    public TTerminal Terminal { get; }

    public string Name { get; }

    public THandle Handle { get; }

    public override sealed Stream Stream { get; }

    public override sealed TextWriter TextWriter { get; }

    public override sealed bool IsValid { get; }

    public override sealed bool IsInteractive { get; }

    protected NativeTerminalWriter(TTerminal terminal, string name, THandle handle)
    {
        Terminal = terminal;
        Name = name;
        Handle = handle;
        Stream = new SynchronizedStream(new TerminalOutputStream(this));
        TextWriter =
            new SynchronizedTextWriter(new StreamWriter(Stream, Cathode.Terminal.Encoding, WriteBufferSize, true)
            {
                AutoFlush = true,
            });
        IsValid = terminal.IsHandleValid(handle, true);
        IsInteractive = terminal.IsHandleInteractive(handle);
    }

    protected abstract int WritePartialNative(scoped ReadOnlySpan<byte> buffer, CancellationToken cancellationToken);

    protected override sealed int WritePartialCore(scoped ReadOnlySpan<byte> buffer)
    {
        return WritePartialNative(buffer, default);
    }

    protected override sealed ValueTask<int> WritePartialCoreAsync(
        ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        // We currently have no native async support.
        return cancellationToken.IsCancellationRequested
            ? ValueTask.FromCanceled<int>(cancellationToken)
            : new(Task.Run(() => WritePartialNative(buffer.Span, cancellationToken), cancellationToken));
    }
}

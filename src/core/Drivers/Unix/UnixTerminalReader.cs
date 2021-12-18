using static System.Unix.UnixPInvoke;

namespace System.Drivers.Unix;

sealed class UnixTerminalReader : DriverTerminalReader<UnixTerminalDriver, int>
{
    readonly object _lock;

    readonly UnixCancellationPipe _cancellationPipe;

    public UnixTerminalReader(
        UnixTerminalDriver driver,
        string name,
        int handle,
        UnixCancellationPipe cancellationPipe,
        object @lock)
        : base(driver, name, handle)
    {
        _lock = @lock;
        _cancellationPipe = cancellationPipe;
    }

    protected override unsafe int ReadBufferCore(Span<byte> buffer, CancellationToken cancellationToken)
    {
        // If the descriptor is invalid, just present the illusion to the user that it has been redirected to /dev/null
        // or something along those lines, i.e. return EOF.
        if (buffer.IsEmpty || !IsValid)
            return 0;

        lock (_lock)
        {
            _cancellationPipe.PollWithCancellation(Handle, cancellationToken);

            fixed (byte* p = &MemoryMarshal.GetReference(buffer))
            {
                nint ret;

                // Note that this call may get us suspended by way of a SIGTTIN signal if we are a background process
                // and the handle refers to a terminal.
                while ((ret = read(Handle, p, (nuint)buffer.Length)) == -1 &&
                    Marshal.GetLastPInvokeError() == EINTR)
                {
                    // Retry in case we get interrupted by a signal.
                }

                if (ret != -1)
                    return (int)ret;

                var err = Marshal.GetLastPInvokeError();

                // EPIPE means the descriptor was probably redirected to a program that ended.
                return err == EPIPE ?
                    0 : throw new TerminalException($"Could not read from {Name}: {new Win32Exception(err).Message}");
            }
        }
    }
}

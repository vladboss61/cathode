using Vezel.Cathode.Unix;
using Vezel.Cathode.Unix.Linux;
using static Vezel.Cathode.Unix.Linux.LinuxPInvoke;
using static Vezel.Cathode.Unix.UnixPInvoke;

namespace Vezel.Cathode.Terminals.Unix.Linux;

internal sealed class LinuxVirtualTerminal : UnixVirtualTerminal
{
    // Keep this class in sync with the MacOSVirtualTerminal class.

    public static LinuxVirtualTerminal Instance { get; } = new();

    private Termios? _original;

    private LinuxVirtualTerminal()
    {
    }

    protected override TerminalSize? QuerySize()
    {
        return ioctl(TerminalOut.Handle, TIOCGWINSZ, out var w) == 0 ? new(w.ws_col, w.ws_row) : null;
    }

    protected override unsafe void SetModeCore(bool raw, bool flush)
    {
        if (tcgetattr(TerminalOut.Handle, out var termios) == -1)
            throw new TerminalNotAttachedException();

        // Stash away the original settings the first time we are successfully called.
        _original ??= termios;

        // These values are usually the default, but we set them just to be safe since UnixTerminalReader would not
        // behave as expected by callers if these values differ.
        termios.c_cc[VTIME] = 0;
        termios.c_cc[VMIN] = 1;

        // Turn off some features that make little or no sense for virtual terminals.
        termios.c_iflag &= ~(IGNBRK | IGNPAR | PARMRK | INPCK | ISTRIP | IXOFF | IMAXBEL);
        termios.c_oflag &= ~(OFILL | OFDEL | NLDLY | CRDLY | TABDLY | BSDLY | VTDLY | FFDLY);
        termios.c_oflag |= NL0 | CR0 | TAB0 | BS0 | VT0 | FF0;
        termios.c_cflag &= ~(CSTOPB | PARENB | PARODD | HUPCL | CLOCAL | CMSPAR | CRTSCTS);
        termios.c_lflag &= ~(FLUSHO | EXTPROC);

        // Set up some sensible defaults.
        termios.c_iflag &= ~(IGNCR | INLCR | IUCLC | IXANY);
        termios.c_iflag |= IUTF8;
        termios.c_oflag &= ~(OLCUC | OCRNL | ONOCR | ONLRET);
        termios.c_cflag &= ~CSIZE;
        termios.c_cflag |= CS8 | CREAD;
        termios.c_lflag &= ~(XCASE | ECHONL | NOFLSH | ECHOPRT | PENDIN);

        var iflag = BRKINT | ICRNL | IXON;
        var oflag = OPOST | ONLCR;
        var lflag = ISIG | ICANON | ECHO | ECHOE | ECHOK | ECHOCTL | ECHOKE | IEXTEN;

        // Finally, enable/disable features that depend on raw/cooked mode.
        if (raw)
        {
            termios.c_iflag &= ~iflag;
            termios.c_oflag &= ~oflag;
            termios.c_lflag &= ~lflag;
            termios.c_lflag |= TOSTOP;
        }
        else
        {
            termios.c_iflag |= iflag;
            termios.c_oflag |= oflag;
            termios.c_lflag |= lflag;
            termios.c_lflag &= ~TOSTOP;
        }

        int ret;
        var err = 0;

        using (var guard = raw ? null : new PosixSignalGuard(PosixSignal.SIGTTOU))
        {
            while ((ret = tcsetattr(TerminalOut.Handle, flush ? TCSAFLUSH : TCSANOW, termios)) == -1 &&
                (err = Marshal.GetLastPInvokeError()) == EINTR)
            {
                // Retry in case we get interrupted by a signal. If we are trying to switch to cooked mode and we saw
                // SIGTTOU, it means we are a background process. We will trust that, by the time we actually read or
                // write anything, we will be in cooked mode.
                if (guard?.Signaled == true)
                    return;
            }
        }

        if (ret != 0)
            throw new TerminalConfigurationException(
                $"Could not change terminal mode: {new Win32Exception(err).Message}");
    }

    public override int OpenTerminalHandle(string name)
    {
        return open(name, O_RDWR | O_NOCTTY | O_CLOEXEC);
    }

    public override bool PollHandles(int? error, short events, scoped Span<int> handles)
    {
        if (error is int err && err != EAGAIN)
            return false;

        var fds = (stackalloc Pollfd[handles.Length]);

        for (var i = 0; i < handles.Length; i++)
            fds[i] = new Pollfd
            {
                fd = handles[i],
                events = events,
            };

        int ret;

        while ((ret = poll(fds, (nuint)fds.Length, -1)) == -1 && Marshal.GetLastPInvokeError() == EINTR)
        {
            // Retry in case we get interrupted by a signal.
        }

        if (ret == -1)
            return false;

        for (var i = 0; i < handles.Length; i++)
            handles[i] = fds[i].revents;

        return true;
    }

    public override void DangerousRestoreSettings()
    {
        using var guard = Control.Guard();

        if (_original is Termios tios)
            _ = tcsetattr(TerminalOut.Handle, TCSAFLUSH, tios);
    }
}

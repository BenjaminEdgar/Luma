using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace Luma.App.Services;

public sealed class GlobalShortcutService : IDisposable
{
    private const int ShortcutId = 0x4C55;
    private const uint WmHotkey = 0x0312;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const uint VkE = 0x45;
    private const uint RemoveMessage = 0x0001;

    private DispatcherTimer? _timer;
    private Action? _action;
    private bool _registered;

    public bool Start(Action action)
    {
        if (!OperatingSystem.IsWindows() || _registered) return false;
        if (!RegisterHotKey(IntPtr.Zero, ShortcutId, ModControl | ModShift | ModNoRepeat, VkE)) return false;
        _registered = true;
        _action = action;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Input, (_, _) => DrainMessages());
        _timer.Start();
        return true;
    }

    private void DrainMessages()
    {
        while (PeekMessage(out var message, IntPtr.Zero, WmHotkey, WmHotkey, RemoveMessage))
            if (message.WParam.ToUInt64() == ShortcutId) _action?.Invoke();
    }

    public void Dispose()
    {
        _timer?.Stop();
        _timer = null;
        _action = null;
        if (_registered && OperatingSystem.IsWindows()) UnregisterHotKey(IntPtr.Zero, ShortcutId);
        _registered = false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr Window;
        public uint Message;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public NativePoint Point;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out NativeMessage message, IntPtr window, uint minimum, uint maximum, uint remove);
}

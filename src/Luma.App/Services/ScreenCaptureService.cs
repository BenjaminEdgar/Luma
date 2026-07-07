using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Luma.App.Services;

public interface IScreenCaptureService
{
    Task<string?> CaptureRegionAsync(Window owner, CancellationToken cancellationToken);
    Task<string> CaptureScreenAsync(Window owner, CancellationToken cancellationToken);
}

public sealed class ScreenCaptureService : IScreenCaptureService
{
    public async Task<string?> CaptureRegionAsync(Window owner, CancellationToken cancellationToken)
    {
        var selection = await SelectionWindow.SelectAsync(owner, cancellationToken);
        if (selection is null || selection.Value.Width < 3 || selection.Value.Height < 3) return null;
        return await CaptureRectAsync(selection.Value, cancellationToken);
    }

    public async Task<string> CaptureScreenAsync(Window owner, CancellationToken cancellationToken)
    {
        var screen = owner.Screens.ScreenFromWindow(owner) ?? owner.Screens.Primary
            ?? throw new InvalidOperationException("No screen available to capture.");
        return await CaptureRectAsync(screen.Bounds, cancellationToken);
    }

    private static async Task<string> CaptureRectAsync(PixelRect rect, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Luma");
        Directory.CreateDirectory(directory);
        Cleanup(directory);
        var output = Path.Combine(directory, $"capture-{Guid.NewGuid():N}.png");

        if (OperatingSystem.IsWindows()) CaptureWindows(rect, output);
        else if (OperatingSystem.IsMacOS())
            await RunCaptureTool("/usr/sbin/screencapture", ["-x", $"-R{rect.X},{rect.Y},{rect.Width},{rect.Height}", output], cancellationToken);
        else
            await CaptureLinux(rect, output, cancellationToken);
        return output;
    }

    private static async Task CaptureLinux(PixelRect r, string output, CancellationToken token)
    {
        var grim = FindOnPath("grim");
        if (grim is not null)
            await RunCaptureTool(grim, ["-g", $"{r.X},{r.Y} {r.Width}x{r.Height}", output], token);
        else
        {
            var import = FindOnPath("import") ?? throw new InvalidOperationException("Install grim (Wayland) or ImageMagick import (X11) to capture the screen.");
            await RunCaptureTool(import, ["-window", "root", "-crop", $"{r.Width}x{r.Height}+{r.X}+{r.Y}", output], token);
        }
    }

    private static async Task RunCaptureTool(string fileName, IEnumerable<string> args, CancellationToken token)
    {
        var psi = new ProcessStartInfo(fileName) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        await process.WaitForExitAsync(token);
        if (process.ExitCode != 0) throw new InvalidOperationException($"Screen capture exited with code {process.ExitCode}.");
    }

    private static string? FindOnPath(string command)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path.Split(Path.PathSeparator).Select(p => Path.Combine(p, command)).FirstOrDefault(File.Exists);
    }

    private static void Cleanup(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "capture-*.png"))
            try { if (File.GetCreationTimeUtc(file) < DateTime.UtcNow.AddDays(-1)) File.Delete(file); } catch { }
    }

    private static void CaptureWindows(PixelRect rect, string output)
    {
        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, rect.Width, rect.Height);
        var old = SelectObject(memory, bitmap);
        try
        {
            if (!BitBlt(memory, 0, 0, rect.Width, rect.Height, screen, rect.X, rect.Y, 0x00CC0020))
                throw new InvalidOperationException("Windows screen capture failed.");
            var info = new BitmapInfo { Header = new BitmapInfoHeader { Size = 40, Width = rect.Width, Height = -rect.Height, Planes = 1, BitCount = 32, Compression = 0 } };
            var bytes = new byte[rect.Width * rect.Height * 4];
            if (GetDIBits(memory, bitmap, 0, (uint)rect.Height, bytes, ref info, 0) == 0)
                throw new InvalidOperationException("Could not read captured pixels.");

            using var target = new WriteableBitmap(new PixelSize(rect.Width, rect.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using (var frame = target.Lock())
            {
                var sourceStride = rect.Width * 4;
                for (var y = 0; y < rect.Height; y++)
                    Marshal.Copy(bytes, y * sourceStride, frame.Address + y * frame.RowBytes, sourceStride);
            }
            target.Save(output);
        }
        finally
        {
            SelectObject(memory, old); DeleteObject(bitmap); DeleteDC(memory); ReleaseDC(IntPtr.Zero, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfoHeader { public uint Size; public int Width; public int Height; public ushort Planes; public ushort BitCount; public uint Compression; public uint SizeImage; public int XPelsPerMeter; public int YPelsPerMeter; public uint ClrUsed; public uint ClrImportant; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public BitmapInfoHeader Header; public uint Colors; }
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dest, int x, int y, int width, int height, IntPtr src, int srcX, int srcY, uint rop);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] bits, ref BitmapInfo info, uint usage);
}

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;

namespace Luma.App.Services;

public interface IScreenDifferenceService
{
    double Measure(string previousPath, string currentPath);
}

public sealed class ScreenDifferenceService : IScreenDifferenceService
{
    private static readonly PixelSize SampleSize = new(48, 27);

    public double Measure(string previousPath, string currentPath)
    {
        try
        {
            var previous = ReadSample(previousPath);
            var current = ReadSample(currentPath);
            double total = 0;
            var changed = 0;
            for (var index = 0; index < previous.Length; index++)
            {
                var difference = Math.Abs(previous[index] - current[index]) / 255d;
                total += difference;
                if (difference >= .2) changed++;
            }

            var averageDifference = total / previous.Length;
            var changedRatio = changed / (double)previous.Length;
            return Math.Max(averageDifference, changedRatio * .5);
        }
        catch { return 0; }
    }

    private static byte[] ReadSample(string path)
    {
        using var source = new Bitmap(path);
        using var scaled = source.CreateScaledBitmap(SampleSize);
        var stride = SampleSize.Width * 4;
        var bufferSize = stride * SampleSize.Height;
        var pointer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            scaled.CopyPixels(new PixelRect(0, 0, SampleSize.Width, SampleSize.Height), pointer, bufferSize, stride);
            var pixels = new byte[bufferSize];
            Marshal.Copy(pointer, pixels, 0, bufferSize);
            var luminance = new byte[SampleSize.Width * SampleSize.Height];
            for (var sourceIndex = 0; sourceIndex < pixels.Length; sourceIndex += 4)
            {
                var blue = pixels[sourceIndex];
                var green = pixels[sourceIndex + 1];
                var red = pixels[sourceIndex + 2];
                luminance[sourceIndex / 4] = (byte)((red * 54 + green * 183 + blue * 19) / 256);
            }
            return luminance;
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }
}

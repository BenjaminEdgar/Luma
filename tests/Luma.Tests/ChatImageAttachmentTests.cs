using Luma.App.Models;

namespace Luma.Tests;

/// <summary>Guards in-chat screenshot attachments on user turns.</summary>
public sealed class ChatImageAttachmentTests
{
    [Fact]
    public void AttachImageCopiesFileAndSetsHasImage()
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), "LumaTests", "img-src-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceDir);
        var source = Path.Combine(sourceDir, "shot.png");
        // Minimal valid 1x1 PNG
        File.WriteAllBytes(source,
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53,
            0xDE, 0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41, 0x54, 0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00,
            0x00, 0x00, 0x03, 0x00, 0x01, 0x00, 0x05, 0xFE, 0xD4, 0xEF, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45,
            0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
        ]);

        using var message = new ChatMessage("user", "Explain this");
        Assert.False(message.HasImage);

        message.AttachImage(source, "Selected area");

        Assert.True(message.HasImage);
        Assert.Equal("Selected area", message.ImageLabel);
        Assert.False(string.IsNullOrWhiteSpace(message.AttachmentPath));
        Assert.True(File.Exists(message.AttachmentPath));

        // Source can be deleted; message still holds its own copy.
        File.Delete(source);
        Assert.True(message.HasImage);
        Assert.True(File.Exists(message.AttachmentPath));

        var owned = message.AttachmentPath!;
        message.Dispose();
        Assert.False(message.HasImage);
        Assert.False(File.Exists(owned));
        try { Directory.Delete(sourceDir, true); } catch { /* ignore */ }
    }

    [Fact]
    public void ChatBubbleTemplateRendersAttachmentImage()
    {
        var xaml = ReadShipped("src/Luma.App/MainWindow.axaml");
        Assert.Contains("HasImage", xaml);
        Assert.Contains("AttachmentImage", xaml);
        Assert.Contains("Classes=\"chatimage\"", xaml);
        Assert.Contains("ImageLabel", xaml);

        var vm = ReadShipped("src/Luma.App/ViewModels/MainWindowViewModel.cs");
        Assert.Contains("AttachCaptureToMessage", vm);
        Assert.Contains("AttachImage", vm);
    }

    private static string ReadShipped(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            if (File.Exists(Path.Combine(dir.FullName, "Luma.slnx")))
            {
                var fromRoot = Path.Combine(dir.FullName, relativePath);
                if (File.Exists(fromRoot)) return File.ReadAllText(fromRoot);
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"Could not locate shipped source {relativePath}");
    }
}

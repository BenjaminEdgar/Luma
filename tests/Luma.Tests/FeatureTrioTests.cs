using Luma.App.Services;

namespace Luma.Tests;

/// <summary>Write audit, clipboard/@files context, and screen-change digest.</summary>
public sealed class FeatureTrioTests
{
    [Fact]
    public void WriteAuditorDetectsCreateModifyAndUndo()
    {
        var root = Path.Combine(Path.GetTempPath(), "LumaTests", "audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var a = Path.Combine(root, "a.txt");
        File.WriteAllText(a, "original\n");
        try
        {
            var snap = WorkspaceWriteAuditor.Capture(root);
            File.WriteAllText(a, "edited\n");
            File.WriteAllText(Path.Combine(root, "b.txt"), "brand new\n");

            var changes = WorkspaceWriteAuditor.Diff(root, snap);
            Assert.Contains(changes, c => c.RelativePath == "a.txt" && c.Kind == FileChangeKind.Modified);
            Assert.Contains(changes, c => c.RelativePath == "b.txt" && c.Kind == FileChangeKind.Created);

            var created = changes.First(c => c.RelativePath == "b.txt");
            WorkspaceWriteAuditor.Undo(root, created);
            Assert.False(File.Exists(Path.Combine(root, "b.txt")));
            Assert.True(created.IsUndone);

            var modified = changes.First(c => c.RelativePath == "a.txt");
            WorkspaceWriteAuditor.Undo(root, modified);
            Assert.Equal("original\n", File.ReadAllText(a).Replace("\r\n", "\n"));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void ContextAttachmentsExtractMentionsAndBuildBlock()
    {
        var root = Path.Combine(Path.GetTempPath(), "LumaTests", "ctx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "Program.cs");
        File.WriteAllText(file, "class Program {}");
        try
        {
            var mentions = ContextAttachments.ExtractMentions("Please read @Program.cs and @\"docs/readme.md\"");
            Assert.Contains("Program.cs", mentions);
            Assert.Contains("docs/readme.md", mentions);

            var resolved = ContextAttachments.ResolvePath(root, "Program.cs");
            Assert.Equal(Path.GetFullPath(file), resolved);

            var ctx = ContextAttachments.BuildTaskContext(
                clipboard: "stack trace here",
                attachedFilePaths: [file],
                workingDirectory: root,
                prompt: "see @Program.cs");
            Assert.NotNull(ctx);
            Assert.Contains("Clipboard context", ctx);
            Assert.Contains("stack trace here", ctx);
            Assert.Contains("Attached file: Program.cs", ctx);
            Assert.Contains("class Program", ctx);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void ScreenDigestParserSplitsBulletsAndActions()
    {
        var raw =
            "- Error dialog about NullReference\n" +
            "- VS Code open on AuthService\n" +
            "\n" +
            "Explain this error\n" +
            "Open the stack trace\n" +
            "Start new chat\n";
        var (summary, actions) = ScreenDigestParser.Parse(raw);
        Assert.Contains("Error dialog", summary);
        Assert.Contains("VS Code", summary);
        Assert.Contains("Explain this error", actions);
        Assert.True(actions.Count >= 2);
    }

    [Fact]
    public void ShippedUiWiresTrioFeatures()
    {
        var xaml = ReadShipped("src/Luma.App/MainWindow.axaml");
        Assert.Contains("writeaudit", xaml);
        Assert.Contains("UndoFileChangeCommand", xaml);
        Assert.Contains("UseClipboardCommand", xaml);
        Assert.Contains("Attach file", xaml);
        Assert.Contains("HasClipboardSnippet", xaml);
        Assert.Contains("ActionChips", xaml);

        var chatVm = ReadShipped("src/Luma.App/ViewModels/MainWindowViewModel.Chat.cs");
        var codeVm = ReadShipped("src/Luma.App/ViewModels/MainWindowViewModel.Code.cs");
        var captureVm = ReadShipped("src/Luma.App/ViewModels/MainWindowViewModel.Capture.cs");
        var attachmentsVm = ReadShipped("src/Luma.App/ViewModels/MainWindowViewModel.Attachments.cs");
        Assert.Contains("WorkspaceWriteAuditor", chatVm + codeVm);
        Assert.Contains("GenerateScreenDigestAsync", captureVm);
        Assert.Contains("BuildTurnContext", attachmentsVm);
        Assert.Contains("ContextAttachments", attachmentsVm);
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

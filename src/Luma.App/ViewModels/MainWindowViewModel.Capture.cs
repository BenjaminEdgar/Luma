using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Luma.App.Models;
using Luma.App.Services;

namespace Luma.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    /// <summary>Grabs the whole screen as background context. Called when the panel opens, while
    /// the window is still the small collapsed dock, so there's no need to hide it first.</summary>
    public async Task RefreshContextAsync()
    {
        if (_busy || _refreshingContext) return;
        _refreshingContext = true;
        try
        {
            var path = await _captureService.CaptureScreenAsync(_owner, _lifetime.Token);
            var difference = _contextPath is null ? 1d : _screenDifference.Measure(_contextPath, path);
            var hadMessages = Messages.Count > 0;
            var bigChange = hadMessages && difference >= .16;
            if (bigChange && NewChatConfirmationRequested is not null && await NewChatConfirmationRequested())
            {
                DisposeMessages();
                Suggestions.Clear();
                hadMessages = false;
            }
            ReplaceCapture(ref _contextPath, path);
            if (bigChange && hadMessages)
                _ = GenerateScreenDigestAsync();
            else
                _ = GenerateSuggestionsAsync(difference);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Messages.Add(new ChatMessage("assistant", $"Screen context capture failed: {ex.Message}") { IsError = true }); }
        finally
        {
            _refreshingContext = false;
            NotifySurfaceStateChanged();
        }
    }

    /// <summary>When the screen changes mid-conversation, summarize what's important + offer action chips.</summary>
    private async Task GenerateScreenDigestAsync()
    {
        if (_contextPath is null || !_diagnosticsReady) return;
        var digest = new ChatMessage("assistant", "Scanning what’s on screen…")
        {
            Caption = "✦ Screen changed",
            IsPending = true,
        };
        Messages.Add(digest);
        string? thumbnailPath = null;
        try
        {
            thumbnailPath = await Task.Run(() => CreateSuggestionThumbnail(_contextPath), _lifetime.Token);
            var request = new AiRequest(
                "The user's screen just changed. Reply with:\n" +
                "1) Up to 3 short bullets of what looks important right now (each line starts with - ).\n" +
                "2) A blank line, then up to 3 verb-led next actions (under 8 words each, one per line).\n" +
                "No preamble.",
                null, thumbnailPath ?? _contextPath, [])
            { TaskKind = TaskKind.Suggest };
            var text = await _clientFactory.Create((AiProvider)SelectedProviderIndex).AskAsync(request, null, _lifetime.Token);
            var (summary, actions) = ScreenDigestParser.Parse(text);
            digest.Text = summary;
            digest.SetActionChips(actions.Concat(["Start new chat"]));
            digest.Caption = "✦ Screen changed";
        }
        catch (Exception ex)
        {
            digest.Text = "Your screen looks different than last time.";
            digest.SetActionChips(["Explain this screen", "Start new chat"]);
            if (!string.IsNullOrWhiteSpace(ex.Message))
                digest.Text += $"\n\n*(Digest unavailable: {ex.Message})*";
        }
        finally
        {
            digest.IsPending = false;
            if (thumbnailPath is not null) { try { File.Delete(thumbnailPath); } catch { } }
        }
    }

    /// <summary>Asks the provider for a few short prompt ideas based on the ambient capture and
    /// shows them as chips. Seeds appear instantly; AI refines them. Failures stay silent.</summary>
    private async Task GenerateSuggestionsAsync(double screenDifference = 1d)
    {
        if (_contextPath is null || Messages.Count > 0 || _busy) return;
        if (!_diagnosticsReady) return;
        if (!AppSettings.Current.SuggestFromScreen) return;
        // Fresh window: reuse recent AI chips (not mere seeds) to keep reopening snappy.
        if (Suggestions.Count > 0 && !SuggestionParser.IsOnlySeeds(Suggestions) &&
            DateTime.UtcNow - _suggestionsAt < TimeSpan.FromSeconds(AppSettings.Current.SuggestionFreshSeconds)) return;
        // If the screen looks the same as when the current chips were made, they're still
        // accurate - skip the provider call (and its screenshot tokens) entirely.
        if (Suggestions.Count > 0 && !SuggestionParser.IsOnlySeeds(Suggestions) &&
            AppSettings.Current.SkipSuggestionsWhenScreenUnchanged &&
            screenDifference < .05) return;

        _suggestCts?.Cancel();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _suggestCts = cts;
        // Instant seeds so the empty landing never waits on the model.
        if (Suggestions.Count == 0 || SuggestionParser.IsOnlySeeds(Suggestions))
            ApplySuggestionChips(SuggestionPrompts.InstantSeeds, markFresh: false);
        IsSuggesting = true;
        string? thumbnailPath = null;
        try
        {
            var contextPath = _contextPath;
            thumbnailPath = await Task.Run(() => CreateSuggestionThumbnail(contextPath), cts.Token);
            var count = AppSettings.Current.SuggestionCount;
            var request = new AiRequest(SuggestionPrompts.FromScreen(count), null, thumbnailPath ?? contextPath, [])
            { TaskKind = TaskKind.Suggest };
            // Progressive: as stream lines arrive, promote the first usable chips early.
            var text = await _clientFactory.Create((AiProvider)SelectedProviderIndex).AskAsync(
                request,
                partial =>
                {
                    if (cts.IsCancellationRequested || Messages.Count > 0 || _busy) return;
                    var early = SuggestionParser.Parse(partial, count);
                    if (early.Count > 0)
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (cts.IsCancellationRequested || Messages.Count > 0 || _busy) return;
                            ApplySuggestionChips(early, markFresh: false);
                        });
                },
                cts.Token);
            if (cts.IsCancellationRequested || Messages.Count > 0 || _busy) return;
            var parsed = SuggestionParser.Parse(text, count);
            if (parsed.Count == 0) return; // keep seeds / early chips rather than blanking
            ApplySuggestionChips(parsed, markFresh: true);
        }
        catch { /* keep seeds */ }
        finally
        {
            if (thumbnailPath is not null) { try { File.Delete(thumbnailPath); } catch { } }
            if (_suggestCts == cts) { _suggestCts = null; IsSuggesting = false; }
            cts.Dispose();
        }
    }

    /// <summary>Rewrites the compose draft in place (does not send). Uses the cheap suggestion path.</summary>
    private async Task ImprovePromptAsync()
    {
        var draft = Question.Trim();
        if (_busy || string.IsNullOrWhiteSpace(draft) || SelectedDiagnostic?.IsAvailable == false) return;

        _improveCts?.Cancel();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _improveCts = cts;
        IsImprovingPrompt = true;
        try
        {
            // Light history so rewrites stay grounded in the current chat when present.
            var history = Messages.Count == 0
                ? Array.Empty<ChatMessage>()
                : Messages.Skip(Math.Max(0, Messages.Count - 6)).ToArray();
            var request = new AiRequest(PromptImprove.BuildRequest(draft), null, null, history)
            {
                TaskKind = TaskKind.ImprovePrompt,
            };
            var text = await _clientFactory.Create((AiProvider)SelectedProviderIndex)
                .AskAsync(request, null, cts.Token);
            if (cts.IsCancellationRequested) return;
            var improved = PromptImprove.Parse(text);
            // Only replace if the user has not edited/sent/cleared the draft while we worked.
            if (!string.IsNullOrWhiteSpace(improved) &&
                string.Equals(Question.Trim(), draft, StringComparison.Ordinal))
                Question = improved;
        }
        catch (OperationCanceledException) { }
        catch { /* leave draft unchanged */ }
        finally
        {
            if (_improveCts == cts) { _improveCts = null; IsImprovingPrompt = false; }
            cts.Dispose();
        }
    }

    private async Task GenerateFollowUpSuggestionsAsync()
    {
        _suggestCts?.Cancel();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _suggestCts = cts;
        IsSuggesting = true;
        try
        {
            var history = Messages.ToArray();
            var count = AppSettings.Current.SuggestionCount;
            var request = new AiRequest(SuggestionPrompts.FollowUp(count), null, null, history)
            { TaskKind = TaskKind.FollowUp };
            var text = await _clientFactory.Create((AiProvider)SelectedProviderIndex).AskAsync(
                request,
                partial =>
                {
                    if (cts.IsCancellationRequested) return;
                    var early = SuggestionParser.Parse(partial, count);
                    if (early.Count > 0)
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (cts.IsCancellationRequested) return;
                            ApplySuggestionChips(early, markFresh: false);
                        });
                },
                cts.Token);
            if (cts.IsCancellationRequested) return;
            var parsed = SuggestionParser.Parse(text, count);
            if (parsed.Count == 0) return;
            ApplySuggestionChips(parsed, markFresh: true);
        }
        catch (OperationCanceledException) { }
        catch { /* leave whatever chips we had */ }
        finally
        {
            if (_suggestCts == cts) { _suggestCts = null; IsSuggesting = false; }
            cts.Dispose();
        }
    }

    private void ApplySuggestionChips(IReadOnlyList<string> chips, bool markFresh)
    {
        if (chips.Count == 0) return;
        Suggestions.Clear();
        foreach (var chip in chips) Suggestions.Add(chip);
        if (markFresh) _suggestionsAt = DateTime.UtcNow;
    }

    /// <summary>Chips only need coarse legibility, so the suggestion request ships a downscaled
    /// copy of the ambient capture instead of the full-resolution screen. Returns null (use the
    /// original) when the capture is already small or downscaling fails.</summary>
    private static string? CreateSuggestionThumbnail(string sourcePath)
    {
        var maxWidth = AppSettings.Current.SuggestionImageMaxWidth;
        try
        {
            using var source = new Bitmap(sourcePath);
            if (source.PixelSize.Width <= maxWidth) return null;
            var height = (int)Math.Round(source.PixelSize.Height * (double)maxWidth / source.PixelSize.Width);
            using var scaled = source.CreateScaledBitmap(new Avalonia.PixelSize(maxWidth, height));
            var path = Path.Combine(Path.GetTempPath(), "Luma", $"suggest-{Guid.NewGuid():N}.png");
            scaled.Save(path);
            return path;
        }
        catch { return null; }
    }
}

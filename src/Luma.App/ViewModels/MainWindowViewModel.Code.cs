using System.Diagnostics;
using Avalonia.Threading;
using Luma.App.Models;
using Luma.App.Services;

namespace Luma.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    /// <summary>Runs a coding request inline and attaches the diff review session to the answer bubble.</summary>
    public async Task RunCodeTurnAsync(string prompt, string repository)
    {
        var provider = (AiProvider)SelectedProviderIndex;
        var providerName = Providers[SelectedProviderIndex];
        var user = new ChatMessage("user", prompt);
        AttachCaptureToMessage(user, _regionPath, _contextPath);
        Messages.Add(user);
        var answer = new ChatMessage("assistant", string.Empty, isPending: true)
        {
            Caption = $"✦ {providerName} is inspecting the repository",
            Text = "Inspecting repository...",
        };
        answer.TurnMeta = BuildTurnMeta(providerName, "Code", ChatCaptureAttachment.HasVisual(_regionPath, _contextPath), repository);
        Messages.Add(answer);
        SetBusy(true);
        SetActivity("Inspecting repo", "Preparing a coding turn");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _requestCts = cts;
        var stopwatch = Stopwatch.StartNew();
        var ticker = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background,
            (_, _) => answer.Elapsed = $"{stopwatch.Elapsed.TotalSeconds:0.0} s");
        ticker.Start();
        try
        {
            var writeSnapshot = WorkspaceWriteAuditor.Capture(repository);
            BeginLivePair(repository, writeSnapshot, answer);
            var session = new CodeChatSession(answer, _clientFactory, provider, new GitService(), new ShellService(_operations), repository, _regionPath, _contextPath);
            if (PlanProgressTracking)
            {
                // Capture implement state at session start; late posts still flow through PlanCoordinator.
                session.PlanMarkdownReceived = ApplyPlanMarkdownFromAnyThread;
            }
            answer.CodeSession = session;
            await session.RunAsync(prompt, cts.Token);
            SetActivity("Writing files", "Auditing workspace changes");
            answer.Caption = $"✦ {providerName} - {stopwatch.Elapsed.TotalSeconds:0.0} s";
            AttachWriteAudit(answer, writeSnapshot);
            ConsumeEphemeralAttachments();
            _ = GenerateFollowUpSuggestionsAsync();
        }
        catch (OperationCanceledException)
        {
            answer.Caption = $"✦ {providerName} - stopped";
            if (string.IsNullOrWhiteSpace(answer.Text)) answer.Text = "*Stopped.*";
        }
        catch (Exception ex)
        {
            answer.IsError = true;
            answer.Caption = $"✦ {providerName} - error";
            answer.Text = ex.Message;
        }
        finally
        {
            EndLivePairWatch();
            ticker.Stop();
            stopwatch.Stop();
            answer.IsPending = false;
            answer.IsStreaming = false;
            answer.Elapsed = null;
            _requestCts = null;
            SetBusy(false);
        }
    }

    /// <summary>Continues the same code answer/diff card after a clarifying choice is selected.</summary>
    private async Task RunCodeContinuationAsync(ChatMessage answer, CodeChatSession session, string reply)
    {
        SetBusy(true);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _requestCts = cts;
        answer.IsPending = true;
        var stopwatch = Stopwatch.StartNew();
        var ticker = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background,
            (_, _) => answer.Elapsed = $"{stopwatch.Elapsed.TotalSeconds:0.0} s");
        ticker.Start();
        try { await session.ContinueAsync(reply, cts.Token); _ = GenerateFollowUpSuggestionsAsync(); }
        catch (OperationCanceledException) { answer.Caption = "✦ stopped"; }
        catch (Exception ex) { answer.IsError = true; answer.Text = ex.Message; }
        finally
        {
            ticker.Stop();
            stopwatch.Stop();
            answer.IsPending = false;
            answer.IsStreaming = false;
            answer.Elapsed = null;
            _requestCts = null;
            SetBusy(false);
        }
    }
}

using Avalonia.Threading;
using Luma.App.Models;
using Luma.App.Services;

namespace Luma.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void ToggleSplitBrain() => SplitBrainEnabled = !SplitBrainEnabled;

    private void ChooseSplitBrainSide(object? parameter)
    {
        if (parameter is not SplitBrainChoice choice) return;
        if (choice.Message.SplitBrain is null) return;
        choice.Message.SplitBrain.Choose(choice.Side);
        choice.Message.Text = choice.Message.SplitBrain.MergedText;
        if (choice.Side is "A" or "B")
        {
            OutcomeMemory.Record(OutcomeKind.Note, $"Kept {choice.Side} in split-brain",
                choice.Side == "A" ? choice.Message.SplitBrain.ProviderA : choice.Message.SplitBrain.ProviderB);
        }
    }

    private async Task RunSplitBrainTurnAsync(string prompt, bool attachCaptures)
    {
        var providers = AvailableProviderIndices();
        if (providers.Count < 2)
        {
            // Need two brains; fall back to normal turn.
            await RunTurnAsync(prompt, attachCaptures: attachCaptures);
            return;
        }

        var (region, context) = ChatCaptureAttachment.ForFirstRequest(attachCaptures, _regionPath, _contextPath);
        var user = new ChatMessage("user", prompt);
        AttachCaptureToMessage(user, region, context);
        Messages.Add(user);
        var answer = new ChatMessage("assistant", "Running split-brain...", isPending: true)
        {
            Caption = $"✦ {Providers[providers[0]]} + {Providers[providers[1]]}",
        };
        Messages.Add(answer);
        SetBusy(true);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _requestCts = cts;
        var writeSnapshot = WorkspaceWriteAuditor.Capture(WorkingDirectory);
        BeginLivePair(WorkingDirectory, writeSnapshot, answer);
        try
        {
            var taskContext = BuildTurnContext(prompt);
            var history = Messages.Take(Messages.Count - 2).ToArray();
            var localOcr = ChatCaptureAttachment.HasVisual(region, context)
                ? await ResolveOcrForPathsAsync(region, context, cts.Token)
                : null;
            var (provR, provC) = ChatCaptureAttachment.ForProvider(region, context, localOcr);
            var reqA = new AiRequest(SplitBrainPrompts.Explainer(prompt), provR, provC, history)
            { WorkingDirectory = WorkingDirectory, TaskContext = taskContext, LocalOcrContext = localOcr };
            var reqB = new AiRequest(SplitBrainPrompts.Implementer(prompt), provR, provC, history)
            { WorkingDirectory = WorkingDirectory, TaskContext = taskContext, LocalOcrContext = localOcr };
            var clientA = _clientFactory.Create((AiProvider)providers[0]);
            var clientB = _clientFactory.Create((AiProvider)providers[1]);
            var taskA = clientA.AskAsync(reqA, null, cts.Token);
            var taskB = clientB.AskAsync(reqB, null, cts.Token);
            await Task.WhenAll(taskA, taskB);
            var textA = ChatStreamTextPolicy.ApplyFinal(ShowWhereParser.Extract(await taskA).Text).Text;
            var textB = ChatStreamTextPolicy.ApplyFinal(ShowWhereParser.Extract(await taskB).Text).Text;
            var split = new SplitBrainResult
            {
                ProviderA = Providers[providers[0]],
                ProviderB = Providers[providers[1]],
                TextA = textA,
                TextB = textB,
            };
            answer.SplitBrain = split;
            answer.Text = split.MergedText;
            answer.Caption = $"✦ {split.ProviderA} (explain) · {split.ProviderB} (implement)";
            AttachWriteAudit(answer, writeSnapshot);
            ConsumeEphemeralAttachments();
            RefreshOutcomeChips(prompt);
            _ = GenerateFollowUpSuggestionsAsync();
        }
        catch (OperationCanceledException)
        {
            answer.Caption = "✦ split-brain stopped";
            if (string.IsNullOrWhiteSpace(answer.Text)) answer.Text = "*Stopped.*";
        }
        catch (Exception ex)
        {
            answer.IsError = true;
            answer.Caption = "✦ split-brain error";
            answer.Text = ex.Message;
        }
        finally
        {
            EndLivePairWatch();
            answer.IsPending = false;
            answer.IsStreaming = false;
            _requestCts = null;
            SetBusy(false);
        }
    }

    public sealed record SplitBrainChoice(ChatMessage Message, string Side);
}

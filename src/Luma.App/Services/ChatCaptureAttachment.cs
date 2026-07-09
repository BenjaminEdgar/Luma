namespace Luma.App.Services;

/// <summary>
/// Decides whether a chat turn attaches screenshots on the first provider call.
/// Straight typed chat starts text-only so the model can answer quickly and request
/// NEED_SCREEN only when visual evidence is required; explicit screen actions attach captures.
/// </summary>
public static class ChatCaptureAttachment
{
    /// <summary>
    /// Returns the region/context paths to send with the first request.
    /// When <paramref name="attachCaptures"/> is false both are null even if files exist on disk.
    /// </summary>
    public static (string? Region, string? Context) ForFirstRequest(
        bool attachCaptures, string? regionPath, string? contextPath)
        => attachCaptures ? (regionPath, contextPath) : (null, null);

    public static bool HasVisual(string? regionPath, string? contextPath) =>
        regionPath is not null || contextPath is not null;
}

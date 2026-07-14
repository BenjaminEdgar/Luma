namespace Luma.App.Services;

/// <summary>High-visibility OCR pipeline states for the header + banner.</summary>
public enum OcrUiPhase
{
    /// <summary>Local OCR turned off in settings.</summary>
    Disabled,
    /// <summary>Checking for model files on disk.</summary>
    Checking,
    /// <summary>Models missing — OCR cannot run.</summary>
    Offline,
    /// <summary>Models present; waiting for a capture.</summary>
    Idle,
    /// <summary>Grabbing the screen before OCR.</summary>
    Capturing,
    /// <summary>ONNX OCR running on-device right now.</summary>
    Running,
    /// <summary>Last OCR finished with usable text.</summary>
    Ready,
    /// <summary>Last OCR attempt failed; will fall back to vision if needed.</summary>
    Failed,
}

/// <summary>Copy for the status pill and OCR strip (pure, testable).</summary>
public static class OcrUiStatus
{
    public static string PillLabel(OcrUiPhase phase) => phase switch
    {
        OcrUiPhase.Disabled => "OCR off",
        OcrUiPhase.Checking => "OCR…",
        OcrUiPhase.Offline => "OCR offline",
        OcrUiPhase.Idle => "OCR idle",
        OcrUiPhase.Capturing => "Capturing",
        OcrUiPhase.Running => "OCR RUNNING",
        OcrUiPhase.Ready => "OCR ready",
        OcrUiPhase.Failed => "OCR failed",
        _ => "OCR",
    };

    public static string BannerTitle(OcrUiPhase phase) => phase switch
    {
        OcrUiPhase.Disabled => "Local OCR is off",
        OcrUiPhase.Checking => "Checking local OCR models…",
        OcrUiPhase.Offline => "Local OCR offline — models not found",
        OcrUiPhase.Idle => "Local OCR ready (waiting for screen)",
        OcrUiPhase.Capturing => "Capturing screen for on-device OCR…",
        OcrUiPhase.Running => "ON-DEVICE OCR RUNNING",
        OcrUiPhase.Ready => "ON-DEVICE OCR ACTIVE",
        OcrUiPhase.Failed => "Local OCR failed — vision fallback",
        _ => "Local OCR",
    };

    public static string DefaultDetail(OcrUiPhase phase) => phase switch
    {
        OcrUiPhase.Disabled => "Turn on in Settings to read screens without vision tokens.",
        OcrUiPhase.Checking => "Looking for det.onnx + rec.onnx…",
        OcrUiPhase.Offline => "Run: python tools/ocr/download_models.py  ·  or set models folder in Settings.",
        OcrUiPhase.Idle => "Prefer OCR over vision is on — screenshots go to the model only if OCR fails.",
        OcrUiPhase.Capturing => "Hidden briefly so the dock doesn’t cover the screen.",
        OcrUiPhase.Running => "PP-OCR ONNX on your machine — no cloud OCR, no screenshot tokens yet.",
        OcrUiPhase.Ready => "Screen text extracted locally. Chips/explain prefer this over the vision model.",
        OcrUiPhase.Failed => "Could not read text. Will use screenshots if the provider needs the screen.",
        _ => "",
    };

    public static bool IsBusyPhase(OcrUiPhase phase) =>
        phase is OcrUiPhase.Checking or OcrUiPhase.Capturing or OcrUiPhase.Running;

    public static bool IsAlertPhase(OcrUiPhase phase) =>
        phase is OcrUiPhase.Offline or OcrUiPhase.Failed;
}

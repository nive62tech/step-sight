namespace DesktopGuide.Platform;

// A recoverable transport/session failure, never an input authorization failure.
public sealed class TransientGuidanceException(string message) : Exception(message);

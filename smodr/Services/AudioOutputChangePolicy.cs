namespace smodr.Services;

internal static class AudioOutputChangePolicy
{
    public static bool ShouldPause(string? previousDeviceId, string? nextDeviceId,
        bool isDefaultRole, bool playbackRequested) =>
        isDefaultRole && playbackRequested && !string.IsNullOrWhiteSpace(previousDeviceId)
        && !string.Equals(previousDeviceId, nextDeviceId, StringComparison.OrdinalIgnoreCase);
}

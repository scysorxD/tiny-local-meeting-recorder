namespace LocalMeetingNotes.Core.Services;

public sealed class DiskSpaceChecker
{
    public const long DefaultMinimumFreeBytes = 500L * 1024 * 1024;

    public DiskSpaceCheckResult Check(string path, long minimumFreeBytes = DefaultMinimumFreeBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrWhiteSpace(root))
            {
                return DiskSpaceCheckResult.Unavailable("Could not resolve drive root.");
            }

            var info = new DriveInfo(root);
            if (!info.IsReady)
            {
                return DiskSpaceCheckResult.Unavailable($"Drive {root} is not ready.");
            }

            if (info.AvailableFreeSpace < minimumFreeBytes)
            {
                return DiskSpaceCheckResult.Insufficient(info.AvailableFreeSpace, minimumFreeBytes);
            }

            return DiskSpaceCheckResult.Ok(info.AvailableFreeSpace);
        }
        catch (Exception exception)
        {
            return DiskSpaceCheckResult.Unavailable(exception.Message);
        }
    }
}

public sealed record DiskSpaceCheckResult(
    bool IsSufficient,
    bool CouldCheck,
    long AvailableFreeBytes,
    long RequiredFreeBytes,
    string? Message)
{
    public static DiskSpaceCheckResult Ok(long available) =>
        new(true, true, available, DefaultMinimumFreeBytes, null);

    public static DiskSpaceCheckResult Insufficient(long available, long required) =>
        new(false, true, available, required, $"Only {available / (1024 * 1024)} MB free; need at least {required / (1024 * 1024)} MB.");

    public static DiskSpaceCheckResult Unavailable(string message) =>
        new(false, false, 0, DefaultMinimumFreeBytes, message);

    private static long DefaultMinimumFreeBytes => DiskSpaceChecker.DefaultMinimumFreeBytes;
}

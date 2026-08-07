using System.Threading;

namespace LocalMeetingNotes.App.Bootstrap;

public static class SingleInstance
{
    private const string MutexName = @"Local\LocalMeetingNotes.SingleInstance";

    public static bool TryAcquire(out Mutex? mutex)
    {
        mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (createdNew)
        {
            return true;
        }

        mutex.Dispose();
        mutex = null;
        return false;
    }
}

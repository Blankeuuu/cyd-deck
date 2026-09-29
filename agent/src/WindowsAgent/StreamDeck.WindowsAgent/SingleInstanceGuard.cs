namespace StreamDeck.WindowsAgent;

public sealed class SingleInstanceGuard : IDisposable
{
    private const string DefaultInstanceName = "PCDock.WindowsAgent";

    private readonly Mutex? mutex;
    private readonly EventWaitHandle showWindowEvent;
    private bool disposed;

    private SingleInstanceGuard(Mutex? mutex, EventWaitHandle showWindowEvent, bool isPrimaryInstance)
    {
        this.mutex = mutex;
        this.showWindowEvent = showWindowEvent;
        IsPrimaryInstance = isPrimaryInstance;
    }

    public bool IsPrimaryInstance { get; }

    public WaitHandle ShowWindowWaitHandle => showWindowEvent;

    public static SingleInstanceGuard Acquire()
    {
        return Acquire(DefaultInstanceName);
    }

    internal static SingleInstanceGuard Acquire(string instanceName)
    {
        var safeName = instanceName.Replace('\\', '.');
        var mutex = new Mutex(initiallyOwned: true, $@"Local\{safeName}", out var createdNew);
        var showWindowEvent = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, $@"Local\{safeName}.ShowWindow");
        return createdNew
            ? new SingleInstanceGuard(mutex, showWindowEvent, isPrimaryInstance: true)
            : new SingleInstanceGuard(mutex, showWindowEvent, isPrimaryInstance: false);
    }

    public void SignalPrimaryInstance()
    {
        showWindowEvent.Set();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        showWindowEvent.Dispose();
        if (IsPrimaryInstance)
        {
            mutex?.ReleaseMutex();
        }

        mutex?.Dispose();
    }
}

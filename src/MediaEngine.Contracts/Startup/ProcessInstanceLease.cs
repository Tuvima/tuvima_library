using System.Net.Sockets;
using System.Collections.Concurrent;

namespace MediaEngine.Contracts.Startup;

/// <summary>
/// Prevents two copies of the same Tuvima host from racing for the same local
/// ports. The operating system releases the named mutex when a process exits,
/// including after an abnormal termination.
/// </summary>
public sealed class ProcessInstanceLease : IDisposable
{
    public const string EngineLeaseName = "TuvimaLibrary.Engine";
    public const string DashboardLeaseName = "TuvimaLibrary.Dashboard";

    private static readonly ConcurrentDictionary<string, byte> ProcessLeases = new(StringComparer.Ordinal);

    private readonly Mutex? _mutex;
    private readonly string _name;
    private readonly bool _registeredInProcess;
    private bool _ownsLease;

    private ProcessInstanceLease(Mutex? mutex, string name, bool ownsLease, bool registeredInProcess)
    {
        _mutex = mutex;
        _name = name;
        _ownsLease = ownsLease;
        _registeredInProcess = registeredInProcess;
    }

    public bool IsAcquired => _ownsLease;

    public static ProcessInstanceLease TryAcquire(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!ProcessLeases.TryAdd(name, 0))
            return new ProcessInstanceLease(null, name, ownsLease: false, registeredInProcess: false);

        var mutex = new Mutex(initiallyOwned: false, name);
        try
        {
            var ownsLease = mutex.WaitOne(0);
            if (!ownsLease)
                ProcessLeases.TryRemove(name, out _);
            return new ProcessInstanceLease(mutex, name, ownsLease, registeredInProcess: ownsLease);
        }
        catch (AbandonedMutexException)
        {
            // The previous owner terminated without releasing the mutex. The
            // current process owns it now and can safely continue startup.
            return new ProcessInstanceLease(mutex, name, ownsLease: true, registeredInProcess: true);
        }
        catch
        {
            ProcessLeases.TryRemove(name, out _);
            mutex.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_ownsLease)
        {
            _mutex!.ReleaseMutex();
            _ownsLease = false;
        }

        _mutex?.Dispose();
        if (_registeredInProcess)
            ProcessLeases.TryRemove(_name, out _);
    }
}

public static class StartupFailureClassifier
{
    public static bool IsAddressAlreadyInUse(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse })
                return true;
        }

        return false;
    }
}

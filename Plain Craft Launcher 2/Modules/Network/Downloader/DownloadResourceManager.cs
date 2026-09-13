using System.Diagnostics;
using System.Collections.Concurrent;

namespace PCL.Network;

/// <summary>协调所有下载任务共享的连接数、缓冲区和限速额度。</summary>
internal static class DownloadResourceManager
{
    private static readonly AsyncQuota ConnectionQuota = new();
    private static readonly AsyncQuota BufferQuota = new();
    private static readonly ConcurrentDictionary<string, HostQuotaEntry> HostConnectionQuotas = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object BandwidthLock = new();
    private static readonly object SpeedLock = new();
    private static int _activeConnectionCount;
    private static long _speedBytes;
    private static long _speedSnapshotTick = Stopwatch.GetTimestamp();
    private static long _speed;
    private static readonly LinkedList<BandwidthReservation> BandwidthReservations = new();
    private static bool _bandwidthPumpRunning;

    public static int ActiveConnectionCount => Volatile.Read(ref _activeConnectionCount);

    public static long DownloadSpeed
    {
        get
        {
            lock (SpeedLock)
            {
                var now = Stopwatch.GetTimestamp();
                var elapsedTicks = now - _speedSnapshotTick;
                if (elapsedTicks >= Stopwatch.Frequency / 4)
                {
                    var bytes = Interlocked.Exchange(ref _speedBytes, 0);
                    _speed = elapsedTicks > 0
                        ? Math.Max(0L, (long)(bytes * (double)Stopwatch.Frequency / elapsedTicks))
                        : 0L;
                    _speedSnapshotTick = now;
                }

                return _speed;
            }
        }
    }

    internal static void RecordDownloadedBytes(long bytes)
    {
        if (bytes > 0)
            Interlocked.Add(ref _speedBytes, bytes);
    }

    public static async ValueTask<DownloadConnectionLease> AcquireConnectionAsync(string url,
        CancellationToken cancellationToken)
    {
        var host = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;
        var hostEntry = AcquireHostQuotaEntry(host);
        DownloadQuotaLease? hostLease = null;
        try
        {
            hostLease = await hostEntry.Quota.AcquireAsync(1, () => ModNet.NetTaskConnectionsPerHostLimit,
                cancellationToken)
            .ConfigureAwait(false);
            var globalLease = await ConnectionQuota.AcquireAsync(1,
                () => Math.Clamp(ModNet.NetTaskConnectionLimit, 1, ModNet.NetTaskConnectionLimitMax), cancellationToken)
                .ConfigureAwait(false);
            Interlocked.Increment(ref _activeConnectionCount);
            return new DownloadConnectionLease(globalLease, hostLease, hostEntry);
        }
        catch
        {
            hostLease?.Dispose();
            ReleaseHostQuotaEntry(hostEntry);
            throw;
        }
    }

    public static ValueTask<DownloadQuotaLease> ReserveBufferAsync(int bytes, CancellationToken cancellationToken)
    {
        return BufferQuota.AcquireAsync(bytes, () => ModNet.NetTaskBufferBudgetBytes, cancellationToken);
    }

    internal static void ReleaseConnection()
    {
        Interlocked.Decrement(ref _activeConnectionCount);
    }

    public static async Task ThrottleAsync(int bytes, CancellationToken cancellationToken)
    {
        var limit = ModNet.NetTaskSpeedLimitHigh;
        if (limit <= 0 || bytes <= 0)
            return;

        var reservation = new BandwidthReservation(Math.Max(1L,
            (long)Math.Ceiling((double)bytes * Stopwatch.Frequency / limit)));
        lock (BandwidthLock)
        {
            reservation.Node = BandwidthReservations.AddLast(reservation);
            if (!_bandwidthPumpRunning)
            {
                _bandwidthPumpRunning = true;
                _ = PumpBandwidthReservationsAsync();
            }
        }

        try
        {
            await reservation.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            lock (BandwidthLock)
            {
                if (reservation.Node?.List is not null)
                    BandwidthReservations.Remove(reservation.Node);
            }

            throw;
        }
    }

    private static async Task PumpBandwidthReservationsAsync()
    {
        while (true)
        {
            BandwidthReservation? reservation;
            lock (BandwidthLock)
            {
                if (BandwidthReservations.First is null)
                {
                    _bandwidthPumpRunning = false;
                    return;
                }

                reservation = BandwidthReservations.First.Value;
                BandwidthReservations.RemoveFirst();
                reservation.Node = null;
            }

            reservation.Completion.TrySetResult();
            await Task.Delay(TimeSpan.FromSeconds((double)reservation.DurationTicks / Stopwatch.Frequency))
                .ConfigureAwait(false);
        }
    }

    private static HostQuotaEntry AcquireHostQuotaEntry(string host)
    {
        while (true)
        {
            var entry = HostConnectionQuotas.GetOrAdd(host, static key => new HostQuotaEntry(key));
            var referenceAdded = entry.TryAddReference();
            if (referenceAdded)
            {
                if (HostConnectionQuotas.TryGetValue(host, out var current) && ReferenceEquals(entry, current))
                    return entry;

                ReleaseHostQuotaEntry(entry);
                continue;
            }

            if (HostConnectionQuotas.TryGetValue(host, out var retiredEntry) && ReferenceEquals(entry, retiredEntry))
                ((ICollection<KeyValuePair<string, HostQuotaEntry>>)HostConnectionQuotas)
                    .Remove(new KeyValuePair<string, HostQuotaEntry>(host, entry));
        }
    }

    internal static void ReleaseHostQuotaEntry(HostQuotaEntry entry)
    {
        if (!entry.ReleaseReference())
            return;

        ((ICollection<KeyValuePair<string, HostQuotaEntry>>)HostConnectionQuotas)
            .Remove(new KeyValuePair<string, HostQuotaEntry>(entry.Host, entry));
    }

    private sealed class BandwidthReservation(long durationTicks)
    {
        public long DurationTicks { get; } = durationTicks;
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public LinkedListNode<BandwidthReservation>? Node { get; set; }
    }
}

internal sealed class DownloadConnectionLease(DownloadQuotaLease globalLease, DownloadQuotaLease hostLease,
    HostQuotaEntry hostEntry) : IDisposable
{
    private DownloadQuotaLease? _globalLease = globalLease;
    private DownloadQuotaLease? _hostLease = hostLease;
    private HostQuotaEntry? _hostEntry = hostEntry;

    public void Dispose()
    {
        var globalLease = Interlocked.Exchange(ref _globalLease, null);
        if (globalLease is null)
            return;

        DownloadResourceManager.ReleaseConnection();
        globalLease.Dispose();
        Interlocked.Exchange(ref _hostLease, null)?.Dispose();
        var hostEntry = Interlocked.Exchange(ref _hostEntry, null);
        if (hostEntry is not null)
            DownloadResourceManager.ReleaseHostQuotaEntry(hostEntry);
    }
}

internal sealed class DownloadQuotaLease : IDisposable
{
    private AsyncQuota? _quota;
    private readonly long _amount;

    internal DownloadQuotaLease(AsyncQuota quota, long amount)
    {
        _quota = quota;
        _amount = amount;
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _quota, null)?.Release(_amount);
    }
}

internal sealed class AsyncQuota
{
    private readonly object _lock = new();
    private readonly List<TaskCompletionSource> _waiters = new();
    private long _used;

    public async ValueTask<DownloadQuotaLease> AcquireAsync(long amount, Func<long> getCapacity,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);

        while (true)
        {
            TaskCompletionSource? waiter = null;
            lock (_lock)
            {
                var capacity = Math.Max(amount, getCapacity());
                if (_used + amount <= capacity)
                {
                    _used += amount;
                    return new DownloadQuotaLease(this, amount);
                }

                waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add(waiter);
            }

            try
            {
                await waiter.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                lock (_lock)
                    _waiters.Remove(waiter);
                throw;
            }
        }
    }

    public void Release(long amount)
    {
        TaskCompletionSource[] waiters;
        lock (_lock)
        {
            _used = Math.Max(0, _used - amount);
            waiters = _waiters.ToArray();
            _waiters.Clear();
        }

        foreach (var waiter in waiters)
            waiter.TrySetResult();
    }
}

internal sealed class HostQuotaEntry(string host)
{
    private readonly object _lock = new();
    private int _referenceCount;
    private bool _retired;

    public string Host { get; } = host;
    public AsyncQuota Quota { get; } = new();

    public bool TryAddReference()
    {
        lock (_lock)
        {
            if (_retired)
                return false;

            _referenceCount++;
            return true;
        }
    }

    public bool ReleaseReference()
    {
        lock (_lock)
        {
            if (--_referenceCount != 0)
                return false;

            _retired = true;
            return true;
        }
    }
}

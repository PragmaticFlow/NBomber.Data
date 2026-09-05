namespace NBomber.Data.Tests.Infra;

class MemoryMonitor(long baselineMemory)
{
    private const int MemoryMonitorIntervalMs = 50;
    private const double BytesToMB = 1024.0 * 1024.0;

    private long _maxMemoryUsed;
    private bool _isMonitoring;
    private Thread? _monitorThread;

    public long MaxMemoryUsed => _maxMemoryUsed;
    public double MaxMemoryUsedMB => _maxMemoryUsed / BytesToMB;

    public void Start()
    {
        _isMonitoring = true;
        _monitorThread = new Thread(() =>
        {
            while (_isMonitoring)
            {
                var currentMemory = GC.GetTotalMemory(forceFullCollection: false) - baselineMemory;
                if (currentMemory > _maxMemoryUsed)
                {
                    _maxMemoryUsed = currentMemory;
                }

                Thread.Sleep(MemoryMonitorIntervalMs);
            }
        });

        _monitorThread.Start();
    }

    public void Stop()
    {
        _isMonitoring = false;
        _monitorThread?.Join();
        ForceGarbageCollection();
    }
    
    private void ForceGarbageCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NBomber.Contracts;

namespace NBomber.Data;

internal class RandomLargeDataFeed<T> : IAsyncDataFeed<T>
{
    private const int BatchCount = 4;
    private readonly int _batchSize;
    private readonly SqliteDbRepository<T> _db = new();
    private readonly object _switchLock = new object();
    private readonly List<T>[] _batches;
    private volatile int _activeBatchIndex = 0;
    private int _currentIndexInBatch = -1;
    private Task? _nextBatchLoadTask;
    private Serilog.ILogger? _logger;
    private bool _isSmallDataset;

    [ThreadStatic]
    private static Random? _random;
    private static Random RandomInstance => _random ??= new Random();

    public RandomLargeDataFeed(int elementsInMemoryCount)
    {
        elementsInMemoryCount = elementsInMemoryCount > 100 ? elementsInMemoryCount : 100;
        _batchSize = elementsInMemoryCount / BatchCount;
        _batches = new List<T>[BatchCount];
        for (int i = 0; i < BatchCount; i++)
            _batches[i] = new List<T>(_batchSize);
    }

    public void LoadData(Serilog.ILogger logger, IEnumerable<T> data)
    {
        _logger = logger;
        _db.LoadData(data);

        if (_db.DataCount == 0)
            throw new InvalidOperationException("Data source is empty. At least one item is required.");

        _isSmallDataset = _db.DataCount < _batchSize;

        for (int i = 0; i < BatchCount; i++)
            PopulateBatchWithRandomRecords(_batches[i]);
    }

    private void PopulateBatchWithRandomRecords(List<T> batch)
    {
        var ids = new long[_batchSize];
        for (int i = 0; i < _batchSize; i++)
            ids[i] = NextInt64(1, _db.DataCount + 1);

        _db.LoadBatchByIds(batch, ids, 0, _batchSize);
    }

    private static long NextInt64(long minValue, long maxValue)
    {
        var range = (ulong)(maxValue - minValue);
        ulong randomValue = ((ulong)RandomInstance.Next() << 32) | (uint)RandomInstance.Next();
        return (long)(randomValue % range) + minValue;
    }

    public async ValueTask<T> GetNextItem(ScenarioInfo scenarioInfo)
    {
        while (true)
        {
            var index = Interlocked.Increment(ref _currentIndexInBatch);

            if (index < _batchSize)
            {
                var batch = _batches[_activeBatchIndex];
                var item = batch[index];
                return item;
            }

            // Batch exhausted - need to switch
            Task? currentBatchLoadTask = null;

            lock (_switchLock)
            {
                // Double-check after acquiring lock
                if (_currentIndexInBatch >= _batchSize)
                {
                    currentBatchLoadTask = _nextBatchLoadTask;

                    // Get the batch that was just exhausted to reload in background
                    var exhaustedBatchIndex = _activeBatchIndex;

                    // Switch to next batch
                    _activeBatchIndex++;
                    if (_activeBatchIndex >= BatchCount)
                        _activeBatchIndex = 0;

                    // For small datasets, all batches are pre-filled with random items,
                    // so we don't need to reload - just switch between them
                    if (!_isSmallDataset)
                    {
                        // Start loading the exhausted batch in background
                        _nextBatchLoadTask = Task.Run(() => PopulateBatchWithRandomRecords(_batches[exhaustedBatchIndex]));
                    }

                    // Reset index
                    _currentIndexInBatch = -1;
                }
            }

            // Wait OUTSIDE the lock to allow other threads to proceed
            if (currentBatchLoadTask != null)
            {
                if (currentBatchLoadTask.Status != TaskStatus.RanToCompletion)
                    _logger?.Warning("You should use bigger elementsInMemoryCount, because in memory items were exhausted too fast");

                await currentBatchLoadTask;
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_db != null)
            _db.DisposeAsync();

        return default;
    }
}

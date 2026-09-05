using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NBomber.Contracts;

namespace NBomber.Data;

internal class ConstantLargeDataFeed<T> : IAsyncDataFeed<T>
{
    private readonly int _batchSize;
    private readonly SqliteDbRepository<T> _db = new();
    private readonly List<T> _cachedBatch = new();
    private long _cachedBatchEndIndex = 0;
    private Serilog.ILogger? _logger;

    public ConstantLargeDataFeed(int elementsInMemoryCount)
    {
        _batchSize = elementsInMemoryCount > 100 ? elementsInMemoryCount : 100;
    }

    public ValueTask<T> GetNextItem(ScenarioInfo scenarioInfo)
    {
        var index = scenarioInfo.InstanceNumber % _db.DataCount + 1;

        // Check if index is within pre-loaded batch range (1 to _cachedBatchEndIndex)
        if (index <= _cachedBatchEndIndex)
        {
            return new ValueTask<T>(_cachedBatch[(int)(index - 1)]);
        }

        return new ValueTask<T>(_db.GetById(index));
    }

    public void LoadData(IEnumerable<T> data, Serilog.ILogger? logger = null)
    {
        _logger = logger;
        _db.LoadData(data);

        if (_db.DataCount == 0)
            throw new InvalidOperationException("Data source is empty. At least one item is required.");

        // Load initial batch of first N items (most commonly accessed for constant feed)
        var batchSize = Math.Min(_batchSize, _db.DataCount);
        _db.LoadBatch(_cachedBatch, 1, (int)batchSize);
        _cachedBatchEndIndex = batchSize;
    }

    public ValueTask DisposeAsync()
    {
        return _db.DisposeAsync();
    }
}

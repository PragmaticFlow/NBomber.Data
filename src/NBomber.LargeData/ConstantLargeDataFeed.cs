using NBomber.Contracts;

namespace NBomber.LargeData;

internal class ConstantLargeDataFeed<T> : IAsyncDataFeed<T>, IAsyncDisposable
{
    private int _batchSize = 100;
    private readonly SqliteDbRepository<T> _db = new();
    private List<T> _cachedBatch = new();
    private long _cachedBatchEndId = 0;
    private Serilog.ILogger? _logger;

    public ConstantLargeDataFeed(int elementsInMemoryCount)
    {
        _batchSize = elementsInMemoryCount > 100 ? elementsInMemoryCount : 100;
    }

    public ValueTask<T> GetNextItem(ScenarioInfo scenarioInfo)
    {
        var id = scenarioInfo.InstanceNumber % _db.DataCount + 1;

        // Check if ID is within pre-loaded batch range (1 to _cachedBatchEndId)
        if (id <= _cachedBatchEndId)
        {
            return ValueTask.FromResult(_cachedBatch[(int)(id - 1)]);
        }

        return ValueTask.FromResult(_db.GetById(id));
    }

    public void LoadData(Serilog.ILogger logger, IEnumerable<T> data)
    {
        _logger = logger;
        _db.LoadData(data);

        if (_db.DataCount == 0)
            throw new InvalidOperationException("Data source is empty. At least one item is required.");

        // Load initial batch of first N items (most commonly accessed for constant feed)
        var batchSize = Math.Min(_batchSize, _db.DataCount);
        _db.LoadBatch(_cachedBatch, 1, (int)batchSize);
        _cachedBatchEndId = batchSize;
    }

    public async ValueTask DisposeAsync()
    {
        if (_db != null)
            _db.DisposeAsync();
    }
}

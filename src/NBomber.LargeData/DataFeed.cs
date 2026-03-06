using System.Buffers;
using MessagePack;
using Microsoft.Data.Sqlite;
using NBomber.Contracts;

namespace NBomber.LargeData;

/// <summary>
/// Represents an async data source for feeding large test data into a load test scenario.
/// Designed for datasets that are too large to fit in memory.
/// </summary>
/// <typeparam name="T">The type of data items provided by the feed.</typeparam>
public interface IAsyncDataFeed<T> : IAsyncDisposable
{
    /// <summary>
    /// Retrieves the next data item from the feed based on the provided scenario context.
    /// </summary>
    /// <param name="scenarioInfo">Contains contextual information about the executing scenario.</param>
    /// <returns>A single data item of type <typeparamref name="T"/> to be used in the scenario.</returns>
    ValueTask<T> GetNextItem(ScenarioInfo scenarioInfo);

    /// <summary>
    /// Loads data from the provided enumerable into the feed.
    /// Must be called before GetNextItem.
    /// </summary>
    /// <param name="data">The data to load into the feed.</param>
    void LoadData(IEnumerable<T> data);
}

/// <summary>
/// LargeDataFeed helps inject large test data into your load test. It represents a data source.
/// Designed for datasets that are too large to fit in memory.
/// </summary>
public static class LargeDataFeed
{
    /// <summary>
    /// Creates DataFeed that picks constant value per Scenario copy.
    /// Every Scenario copy will have unique constant value.
    /// </summary>
    public static IAsyncDataFeed<T> Constant<T>() =>
        new ConstantLargeDataFeed<T>();

    /// <summary>
    /// Creates DataFeed that randomly picks an item per GetNextItem() invocation.
    /// </summary>
    public static IAsyncDataFeed<T> Random<T>() =>
        new RandomLargeDataFeed<T>();

    /// <summary>
    /// Creates DataFeed that goes back to the top of the sequence once the end is reached.
    /// </summary>
    /// <param name="batchSize">Number of items to load per batch.</param>
    public static IAsyncDataFeed<T> Circular<T>(int batchSize = 1000) =>
        new CircularLargeDataFeed<T>(batchSize);
}

internal class SqliteDbRepository<T> : IAsyncDisposable
{
    internal long DataCount { get; private set; }

    private readonly string _connectionString;
    private readonly string _dbPath;
    private readonly MessagePackSerializerOptions _deserializeOptions = MessagePackSerializerOptions.Standard
            .WithCompression(MessagePackCompression.Lz4BlockArray)
            .WithResolver(MessagePack.Resolvers.ContractlessStandardResolver.Instance);

    internal SqliteDbRepository()
    {
        var nbomberDataDir = "./nbomber_data";
        Directory.CreateDirectory(nbomberDataDir);
        _dbPath = Path.Combine(nbomberDataDir, $"NBomber.Data.{Guid.NewGuid()}.db");
        _connectionString = $"Data Source={_dbPath}";

        InitDb();
    }

    private SqliteConnection GetConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void InitDb()
    {
        using var connection = GetConnection();

        using var pragmaCmd = connection.CreateCommand();
        pragmaCmd.CommandText = @"
            PRAGMA page_size = 4096;
            PRAGMA synchronous = OFF;
            PRAGMA journal_mode = MEMORY;
            PRAGMA temp_store = MEMORY;
            PRAGMA cache_size = -64000;
            PRAGMA mmap_size = 268435456;";
        pragmaCmd.ExecuteNonQuery();

        using var createTableCmd = connection.CreateCommand();
        createTableCmd.CommandText = @"
        CREATE TABLE nbomber_data (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            data BLOB NOT NULL
        )";
        createTableCmd.ExecuteNonQuery();
    }

    internal void LoadData(IEnumerable<T> data)
    {
        using var connection = GetConnection();

        var messagePackOptions = MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4Block);
        const int batchSize = 1000;

        foreach (var batch in data.Chunk(batchSize))
            InsertBatch(connection, batch.ToList(), messagePackOptions);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM nbomber_data";

        DataCount = (long)(cmd.ExecuteScalar() ?? 0);
    }

    private void InsertBatch(SqliteConnection connection, List<T> batch, MessagePackSerializerOptions messagePackOptions)
    {
        using var transaction = connection.BeginTransaction();
        using var insertCmd = connection.CreateCommand();

        insertCmd.CommandText = "INSERT INTO nbomber_data (data) VALUES ($data)";

        var dataParam = insertCmd.CreateParameter();
        dataParam.ParameterName = "$data";
        insertCmd.Parameters.Add(dataParam);

        foreach (var item in batch)
        {
            dataParam.Value = MessagePackSerializer.Serialize(item, messagePackOptions);
            insertCmd.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    internal T GetById(long id)
    {
        using var connection = GetConnection();
        using var selectCmd = connection.CreateCommand();

        selectCmd.CommandText = "SELECT data FROM nbomber_data WHERE id = $id";
        selectCmd.Parameters.AddWithValue("$id", id);

        using var reader = selectCmd.ExecuteReader();
        if (reader.Read())
        {
            var binaryData = (byte[])reader.GetValue(0);
            return MessagePackSerializer.Deserialize<T>(binaryData, _deserializeOptions);
        }

        return default!;
    }

    internal long LoadBatch(List<T> batch, long startId, int batchSize)
    {
        using var connection = GetConnection();

        if (startId > DataCount)
            startId = 1;

        batch.Clear();

        var pooledArray = ArrayPool<T>.Shared.Rent(batchSize);
        try
        {
            if (DataCount < batchSize)
            {
                return LoadBatchWithRepetition(connection, batch, pooledArray, batchSize);
            }

            return LoadBatchNormal(connection, batch, pooledArray, startId, batchSize);
        }
        finally
        {
            ArrayPool<T>.Shared.Return(pooledArray, clearArray: true);
        }
    }

    private long LoadBatchWithRepetition(SqliteConnection connection, List<T> batch, T[] pooledArray, int count)
    {
        var itemsRead = QueryItems(connection, pooledArray, 1, (int)DataCount);

        // Fill batch by repeating items cyclically
        int added = 0;
        while (added < count)
        {
            for (int i = 0; i < itemsRead; i++)
            {
                batch.Add(pooledArray[i]);
                added++;
                if (added >= count)
                    break;
            }
        }

        return 1; // Always restart from beginning for small datasets
    }

    private long LoadBatchNormal(SqliteConnection connection, List<T> batch, T[] pooledArray, long startId, int count)
    {
        var firstPartCount = QueryItems(connection, pooledArray, startId, count);

        for (int i = 0; i < firstPartCount; i++)
        {
            batch.Add(pooledArray[i]);
        }

        if (firstPartCount < count)
        {
            var missingCount = count - firstPartCount;
            // Reuse same pooled array for second part
            var secondPartCount = QueryItems(connection, pooledArray, 1, missingCount);

            for (int i = 0; i < secondPartCount; i++)
            {
                batch.Add(pooledArray[i]);
            }

            return secondPartCount + 1;
        }

        var nextId = startId + count;
        if (nextId > DataCount)
            nextId = 1;

        return nextId;
    }

    private int QueryItems(SqliteConnection connection, T[] pooledArray, long startId, int count)
    {
        using var selectCmd = connection.CreateCommand();

        selectCmd.CommandText = "SELECT data FROM nbomber_data WHERE id >= $startId AND id < $endId ORDER BY id";
        selectCmd.Parameters.AddWithValue("$startId", startId);
        selectCmd.Parameters.AddWithValue("$endId", startId + count);

        using var reader = selectCmd.ExecuteReader();
        var itemsRead = 0;
        while (reader.Read() && itemsRead < count)
        {
            var binaryData = (byte[])reader.GetValue(0);
            pooledArray[itemsRead] = MessagePackSerializer.Deserialize<T>(binaryData, _deserializeOptions);
            itemsRead++;
        }

        return itemsRead;
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            SqliteConnection.ClearAllPools();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }
        catch
        {
        }

        return ValueTask.CompletedTask;
    }
}

internal class ConstantLargeDataFeed<T> : IAsyncDataFeed<T>, IAsyncDisposable
{
    private SqliteDbRepository<T> _db = new();

    public ConstantLargeDataFeed()
    {
    }

    public ValueTask<T> GetNextItem(ScenarioInfo scenarioInfo)
    {
        return ValueTask.FromResult(_db.GetById(scenarioInfo.InstanceNumber % _db.DataCount + 1));
    }

    public void LoadData(IEnumerable<T> data)
    {
        _db.LoadData(data);
    }

    public async ValueTask DisposeAsync()
    {
        if (_db != null)
            await _db.DisposeAsync();
    }
}

internal class RandomLargeDataFeed<T> : IAsyncDataFeed<T>, IAsyncDisposable
{
    private SqliteDbRepository<T> _db = new();
    private Random random = new Random();

    public RandomLargeDataFeed()
    {
    }

    public ValueTask<T> GetNextItem(ScenarioInfo scenarioInfo)
    {
        return ValueTask.FromResult(_db.GetById(random.NextInt64(1, _db.DataCount + 1)));
    }

    public void LoadData(IEnumerable<T> data)
    {
        _db.LoadData(data);
    }

    public async ValueTask DisposeAsync()
    {
        if (_db != null)
            await _db.DisposeAsync();
    }
}

internal class CircularLargeDataFeed<T> : IAsyncDataFeed<T>, IAsyncDisposable
{
    private int BatchSize = 1000;
    private readonly SqliteDbRepository<T> _db = new();
    private readonly object _switchLock = new object();
    private List<T> _batch1;
    private List<T> _batch2;
    private volatile int _activeBatch = 1;
    private int _currentIndexInBatch = -1;
    private long _nextDbIdToLoad = 1;
    private Task? _nextBatchLoadTask;

    public CircularLargeDataFeed(int batchSize)
    {
        BatchSize = batchSize;
        _batch1 = new List<T>(batchSize);
        _batch2 = new List<T>(batchSize);
    }

    public void LoadData(IEnumerable<T> data)
    {
        _db.LoadData(data);
        _nextDbIdToLoad = _db.LoadBatch(_batch1, _nextDbIdToLoad, BatchSize);
        _nextDbIdToLoad = _db.LoadBatch(_batch2, _nextDbIdToLoad, BatchSize);
    }

    public async ValueTask<T> GetNextItem(ScenarioInfo scenarioInfo)
    {
        while (true)
        {
            var index = Interlocked.Increment(ref _currentIndexInBatch);

            if (index < BatchSize)
            {
                var batch = _activeBatch == 1 ? _batch1 : _batch2;
                return batch[index];
            }

            // Batch exhausted - need to switch
            Task? currentBatchLoadTask = null;

            lock (_switchLock)
            {
                // Double-check after acquiring lock
                if (_currentIndexInBatch >= BatchSize)
                {
                    // Capture task to wait OUTSIDE the lock
                    currentBatchLoadTask = _nextBatchLoadTask;

                    // Switch batches
                    _activeBatch = _activeBatch == 1 ? 2 : 1;

                    // Start loading the now-inactive batch in background
                    if (_activeBatch == 1)
                        _nextBatchLoadTask = Task.Run(() => _nextDbIdToLoad = _db.LoadBatch(_batch2, _nextDbIdToLoad, BatchSize));
                    else
                        _nextBatchLoadTask = Task.Run(() => _nextDbIdToLoad = _db.LoadBatch(_batch1, _nextDbIdToLoad, BatchSize));

                    // Reset index
                    _currentIndexInBatch = -1;
                }
            }

            // Wait OUTSIDE the lock to allow other threads to proceed
            if (currentBatchLoadTask != null)
                await currentBatchLoadTask;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_nextBatchLoadTask != null)
            await _nextBatchLoadTask;

        if (_db != null)
            await _db.DisposeAsync();
    }
}

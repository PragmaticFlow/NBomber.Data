using MessagePack;
using Microsoft.Data.Sqlite;
using NBomber.Contracts;
using NBomber.Data;

namespace NBomber.LargeData;

public interface IAsyncDataFeed<T> : IAsyncDisposable
{
    ValueTask<T> GetNextItem(ScenarioInfo scenarioInfo);
    void LoadData(IEnumerable<T> data);
}

public static class LargeDataFeed
{
    public static IAsyncDataFeed<T> InitConstant<T>() =>
        new ConstantLargeDataFeed<T>();

    public static IAsyncDataFeed<T> InitRandom<T>() =>
        new RandomLargeDataFeed<T>();

    public static IAsyncDataFeed<T> InitCircular<T>(int batchSize = 1000) =>
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

    internal long LoadBatch(List<T> batch, long startId, int count)
    {
        using var connection = GetConnection();

        if (startId > DataCount)
            startId = 1;

        batch.Clear();
        var firstPart = QueryItems(connection, startId, count);
        batch.AddRange(firstPart);

        if (firstPart.Length < count)
        {
            var missingCount = count - firstPart.Length;
            var secondPart = QueryItems(connection, 1, missingCount);
            batch.AddRange(secondPart);
            return missingCount + 1;
        }

        var nextId = startId + count;
        if (nextId > DataCount)
            nextId = 1;

        return nextId;
    }

    private T[] QueryItems(SqliteConnection connection, long startId, int count)
    {
        var result = new T[count];
        using var selectCmd = connection.CreateCommand();

        selectCmd.CommandText = "SELECT data FROM nbomber_data WHERE id >= $startId AND id < $endId ORDER BY id";
        selectCmd.Parameters.AddWithValue("$startId", startId);
        selectCmd.Parameters.AddWithValue("$endId", startId + count);

        using var reader = selectCmd.ExecuteReader();
        int itemsRead = 0;
        while (reader.Read() && itemsRead < count)
        {
            var binaryData = (byte[])reader.GetValue(0);
            result[itemsRead] = MessagePackSerializer.Deserialize<T>(binaryData, _deserializeOptions);
            itemsRead++;
        }

        return result;
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

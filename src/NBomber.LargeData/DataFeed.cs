using MessagePack;
using Microsoft.Data.Sqlite;
using NBomber.Contracts;
using NBomber.Data;

namespace NBomber.LargeData;

public static class LargeDataFeed
{
    public static IDataFeed<T> Constant<T>(IEnumerable<T> data) =>
        new ConstantLargeDataFeed<T>(data);

    public static IDataFeed<T> Random<T>(IEnumerable<T> data) =>
        new RandomLargeDataFeed<T>(data);

    public static IDataFeed<T> Circular<T>(IEnumerable<T> data) =>
        new CircularLargeDataFeed<T>(data);
}

public class SqliteDbRepository<T>
{
    internal long DataCount { get; private set; }
    internal long CurrentIndex { get; set; } = 1;

    private SqliteConnection connection;
    private MessagePackSerializerOptions options = MessagePackSerializerOptions.Standard
            .WithCompression(MessagePackCompression.Lz4BlockArray)
            .WithResolver(MessagePack.Resolvers.ContractlessStandardResolver.Instance);    

    internal SqliteDbRepository(IEnumerable<T> data)
    {
        var projectDirectory = Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory).Parent.Parent.Parent.FullName;
        var dbPath = Path.Combine(projectDirectory, $"NBomber.Data.{Guid.NewGuid()}.db");
        var connectionString = $"Data Source={dbPath}";

        InitDb(connectionString);
        InitData(data);
    }

    private void InitDb(string connectionString)
    {
        connection = new SqliteConnection(connectionString);
        connection.Open();

        // Performance optimizations
        var pragmaCmd = connection.CreateCommand();
        pragmaCmd.CommandText = @"
        PRAGMA synchronous = OFF;
        PRAGMA journal_mode = MEMORY;
        PRAGMA temp_store = MEMORY;
        PRAGMA locking_mode = EXCLUSIVE;
        PRAGMA cache_size = -64000;";
        pragmaCmd.ExecuteNonQuery();

        var createTableCmd = connection.CreateCommand();
        createTableCmd.CommandText = @"
        CREATE TABLE nbomber_data (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            data BLOB NOT NULL
        )";
        createTableCmd.ExecuteNonQuery();
    }


    private void InitData(IEnumerable<T> data)
    {
        var messagePackOptions = MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4Block);
        const int batchSize = 1000;

        foreach (var batch in data.Chunk(batchSize))
            InsertBatch(batch.ToList(), messagePackOptions);

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM nbomber_data";

        DataCount = (long)cmd.ExecuteScalar();
    }

    private void InsertBatch(List<T> batch, MessagePackSerializerOptions messagePackOptions)
    {
        using (var transaction = connection.BeginTransaction())
        {
            var insertCmd = connection.CreateCommand();
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
    }

    internal T GetById(long id)
    {
        var selectCmd = connection.CreateCommand();
        selectCmd.CommandText = "SELECT data FROM nbomber_data WHERE id = $id";
        selectCmd.Parameters.AddWithValue("$id", id);

        using (var reader = selectCmd.ExecuteReader())
        {
            if (reader.Read())
            {
                var binaryData = (byte[])reader.GetValue(0);
                return MessagePackSerializer.Deserialize<T>(binaryData, options);
            }
        }

        return default;
    }
}

internal class ConstantLargeDataFeed<T> : IDataFeed<T>
{
    private SqliteDbRepository<T> _db;
    public T[] Items => [];

    public ConstantLargeDataFeed(IEnumerable<T> data)
    {
        _db = new SqliteDbRepository<T>(data);
    }

    public T GetNextItem(ScenarioInfo scenarioInfo)
    {
        return _db.GetById(scenarioInfo.InstanceNumber % _db.DataCount + 1);
    }
}

internal class RandomLargeDataFeed<T> : IDataFeed<T>
{
    private SqliteDbRepository<T> _db;
    private Random random = new Random();
    public T[] Items => [];

    public RandomLargeDataFeed(IEnumerable<T> data)
    {
        _db = new SqliteDbRepository<T>(data);
    }

    public T GetNextItem(ScenarioInfo scenarioInfo)
    {
        return _db.GetById(random.NextInt64(1, _db.DataCount + 1));
    }
}

internal class CircularLargeDataFeed<T> : IDataFeed<T>
{
    private SqliteDbRepository<T> _db;
    private readonly object _lockObj = new object();
    public T[] Items => [];

    public CircularLargeDataFeed(IEnumerable<T> data)
    {
        _db = new SqliteDbRepository<T>(data);
    }

    public T GetNextItem(ScenarioInfo scenarioInfo)
    {
        lock (_lockObj)
        {
            if (_db.CurrentIndex < _db.DataCount)
                _db.CurrentIndex++;
            else
                _db.CurrentIndex = 1;

            return _db.GetById(_db.CurrentIndex);
        }
    }
}

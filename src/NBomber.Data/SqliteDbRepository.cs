using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace NBomber.Data
{
    internal class SqliteDbRepository<T> : IAsyncDisposable
    {
        internal long DataCount { get; private set; }

        private readonly string _connectionString;
        private readonly string _dbPath;
        private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

        internal SqliteDbRepository()
        {
            var nbomberDataDir = "./nbomber_data";
            Directory.CreateDirectory(nbomberDataDir);
            _dbPath = Path.Combine(nbomberDataDir, $"NBomber.Data.{Guid.NewGuid()}.db");

            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = _dbPath,
                Pooling = true,
                Mode = SqliteOpenMode.ReadOnly
            }.ToString();

            InitDb();
        }

        private SqliteConnection GetConnection()
        {
            var connection = new SqliteConnection(_connectionString);
            connection.Open();
            return connection;
        }

        private SqliteConnection GetWriteConnection()
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = _dbPath,
                Pooling = true,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString();

            var connection = new SqliteConnection(connectionString);
            connection.Open();
            return connection;
        }

        private void InitDb()
        {
            using var connection = GetWriteConnection();

            using var pragmaCmd = connection.CreateCommand();
            pragmaCmd.CommandText = @"
                PRAGMA page_size = 4096;
                PRAGMA synchronous = OFF;
                PRAGMA journal_mode = OFF;
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
            using var connection = GetWriteConnection();

            const int batchSize = 1000;

            foreach (var batch in ChunkEnumerable(data, batchSize))
                InsertBatch(connection, batch);

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM nbomber_data";

            DataCount = (long)(cmd.ExecuteScalar() ?? 0);
        }

        private void InsertBatch(SqliteConnection connection, List<T> batch)
        {
            using var transaction = connection.BeginTransaction();
            using var insertCmd = connection.CreateCommand();

            insertCmd.CommandText = "INSERT INTO nbomber_data (data) VALUES ($data)";

            var dataParam = insertCmd.CreateParameter();
            dataParam.ParameterName = "$data";
            insertCmd.Parameters.Add(dataParam);

            foreach (var item in batch)
            {
                dataParam.Value = JsonSerializer.SerializeToUtf8Bytes(item, _jsonOptions);
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
                return JsonSerializer.Deserialize<T>(binaryData, _jsonOptions);
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
                pooledArray[itemsRead] = JsonSerializer.Deserialize<T>(binaryData, _jsonOptions);
                itemsRead++;
            }

            return itemsRead;
        }

        internal void LoadBatchByIds(List<T> batch, long[] ids, int startIndex, int count)
        {
            using var connection = GetConnection();
            batch.Clear();

            var actualCount = Math.Min(count, ids.Length - startIndex);
            if (actualCount <= 0) return;

            // Get unique IDs and build lookup from ID to deserialized item
            var uniqueIds = new HashSet<long>();
            for (int i = 0; i < actualCount; i++)
                uniqueIds.Add(ids[startIndex + i]);

            var idList = string.Join(",", uniqueIds);

            using var selectCmd = connection.CreateCommand();
            selectCmd.CommandText = $"SELECT id, data FROM nbomber_data WHERE id IN ({idList})";

            var itemLookup = new Dictionary<long, T>();
            using var reader = selectCmd.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetInt64(0);
                var binaryData = (byte[])reader.GetValue(1);
                var item = JsonSerializer.Deserialize<T>(binaryData, _jsonOptions);
                itemLookup[id] = item;
            }

            // Fill batch in order, handling duplicates properly
            for (int i = 0; i < actualCount; i++)
            {
                var id = ids[startIndex + i];
                if (itemLookup.TryGetValue(id, out var item))
                    batch.Add(item);
            }
        }

        private static IEnumerable<List<T>> ChunkEnumerable(IEnumerable<T> source, int chunkSize)
        {
            var chunk = new List<T>(chunkSize);
            foreach (var item in source)
            {
                chunk.Add(item);
                if (chunk.Count == chunkSize)
                {
                    yield return chunk;
                    chunk = new List<T>(chunkSize);
                }
            }
            if (chunk.Count > 0)
                yield return chunk;
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                SqliteConnection.ClearAllPools();

                if (File.Exists(_dbPath))
                    File.Delete(_dbPath);
            }
            catch { }

            return default;
        }
    }
}

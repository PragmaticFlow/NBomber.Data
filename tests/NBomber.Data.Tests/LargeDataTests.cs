using System.Diagnostics;
using Microsoft.Data.Sqlite;
using NBomber.CSharp;
using NBomber.LargeData;

namespace NBomber.Data.Tests;

public class LargeDataTests
{
    private const string TestCsvFile = "users-feed-data.csv";
    private const string TestJsonFile = "users-feed-data.json";
    private const double BytesToMB = 1024.0 * 1024.0;
    private const int MemoryMonitorIntervalMs = 50;

    private static TestSettings Settings => TestSettings.Instance;

    [Fact]
    public async Task CsvStream_InitData_Should_Not_Use_Memory_Above_Limit()
    {
        DataGenerator.GenerateLargeCsvFile(TestCsvFile, Settings.TestFileSizeBytes);

        var baselineMemory = GC.GetTotalMemory(forceFullCollection: true);
        var monitor = new MemoryMonitor(baselineMemory);

        monitor.Start();

        await using var dataFeed = LargeDataFeed.Random<TestUser>();
        using var csvStream = LargeData.LargeData.OpenCsvStream<TestUser>(TestCsvFile);
        dataFeed.LoadData(csvStream);

        monitor.Stop();

        var maxMemoryUsed = monitor.MaxMemoryUsed;

        var actualMemoryMb = maxMemoryUsed / BytesToMB;

        Assert.True(maxMemoryUsed < Settings.MaxAllowedMemoryBytes,
            $"Memory usage {actualMemoryMb:F2} MB exceeded {Settings.MaxAllowedMemoryMB} MB limit");

        CleanupTestResources(TestCsvFile);
    }

    [Fact]
    public async Task JsonStream_InitData_Should_Not_Use_Memory_Above_Limit()
    {
        DataGenerator.GenerateLargeJsonFile(TestJsonFile, Settings.TestFileSizeBytes);

        var baselineMemory = GC.GetTotalMemory(forceFullCollection: true);
        var monitor = new MemoryMonitor(baselineMemory);

        monitor.Start();

        await using var dataFeed = LargeDataFeed.Random<TestUser>();
        using var jsonStream = LargeData.LargeData.OpenJsonStream<TestUser>(TestJsonFile);
        dataFeed.LoadData(jsonStream);

        monitor.Stop();

        var maxMemoryUsed = monitor.MaxMemoryUsed;

        var actualMemoryMb = maxMemoryUsed / BytesToMB;

        Assert.True(maxMemoryUsed < Settings.MaxAllowedMemoryBytes,
            $"Memory usage {actualMemoryMb:F2} MB exceeded {Settings.MaxAllowedMemoryMB} MB limit");

        CleanupTestResources(TestJsonFile);
    }

    [Fact]
    public async Task CircularDataFeed_GetNext_Should_Circular_Loop_Over_All_Data()
    {
        DataGenerator.GenerateLargeCsvFile(TestCsvFile, Settings.TestFileSizeBytes);
        var fullLoopCompletedTimes = 0;

        var totalRows = GetCsvRowCount(TestCsvFile);

        await using var dataFeed = LargeDataFeed.Circular<TestUser>();
        using var csvStream = LargeData.LargeData.OpenCsvStream<TestUser>(TestCsvFile);
        dataFeed.LoadData(csvStream);

        var invocationCount = 0L;

        var scenario = Scenario.Create("scenario", async context =>
        {
            var item = await dataFeed.GetNextItem(context.ScenarioInfo);

            invocationCount++;

            // Check if we completed a full loop (every totalRows iterations)
            if (invocationCount % totalRows == 0)
                fullLoopCompletedTimes++;

            if (fullLoopCompletedTimes >= 3)
                context.StopScenario("scenario", $"Completed {fullLoopCompletedTimes} loops");

            return Response.Ok();
        })
        .WithoutWarmUp()
        .WithLoadSimulations(Simulation.KeepConstant(copies: 1, during: TimeSpan.FromHours(1)));

        NBomberRunner
            .RegisterScenarios(scenario)
            .Run();

        CleanupTestResources(TestCsvFile);
    }

    [Fact]
    public async Task Concurrency_Circular_GetNext_Should_Not_Use_Memory_Above_Limit()
    {
        DataGenerator.GenerateLargeCsvFile(TestCsvFile, Settings.TestFileSizeBytes);

            //var data = Data.LoadCsv<TestUser>(TestCsvFile);
            //var feed = DataFeed.Circular(data);

            var totalRows = GetCsvRowCount(TestCsvFile);

            await using var dataFeed = LargeDataFeed.Circular<TestUser>(1000);
            using var csvStream = LargeData.LargeData.OpenCsvStream<TestUser>(TestCsvFile);
            dataFeed.LoadData(csvStream);

            var baselineMemory = GC.GetTotalMemory(forceFullCollection: true);
            var monitor = new MemoryMonitor(baselineMemory);
            var maxExecutionTimeMs = 0.0;

            var scenario = Scenario.Create("scenario", async context =>
            {
                var sw = Stopwatch.StartNew();

                var item = await dataFeed.GetNextItem(context.ScenarioInfo);
                // var item = feed.GetNextItem(context.ScenarioInfo);

                sw.Stop();
                var elapsedMs = sw.Elapsed.TotalMilliseconds;

                context.Logger.Debug($"GetNextItem execution time: {item.Id} {elapsedMs:F2} ms");

                if (elapsedMs > maxExecutionTimeMs)
                    maxExecutionTimeMs = elapsedMs;

                return Response.Ok();
            })
            .WithInit(context =>
            {
                monitor.Start();
                return Task.CompletedTask;
            })
            .WithClean(context =>
            {
                monitor.Stop();
                CleanupTestResources(TestCsvFile);
                return Task.CompletedTask;
            })
            .WithWarmUpDuration(TimeSpan.FromSeconds(10))
            .WithLoadSimulations(Simulation.IterationsForConstant(copies: 100, iterations: (int)totalRows));

            NBomberRunner
                .RegisterScenarios(scenario)
                .Run();

            var maxMemoryUsed = monitor.MaxMemoryUsed;
            var actualMemoryMb = maxMemoryUsed / BytesToMB;            

            Assert.True(maxMemoryUsed < Settings.MaxAllowedMemoryBytesConcurrency,
                $"Memory usage {actualMemoryMb:F2} MB exceeded {Settings.MaxAllowedMemoryMBConcurrency} MB limit");

            Assert.True(maxExecutionTimeMs <= Settings.MaxExecutionTimeMs,
                $"Execution time {maxExecutionTimeMs:F2} ms exceeded {Settings.MaxExecutionTimeMs} ms limit");
    }

    private class MemoryMonitor(long baselineMemory)
    {
        private long _maxMemoryUsed;
        private bool _isMonitoring;
        private Thread? _monitorThread;

        public long MaxMemoryUsed => _maxMemoryUsed;

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
    }

    private static void CleanupTestResources(string sourceFile)
    {
        ForceGarbageCollection();
        Thread.Sleep(500);

        CleanupSourceFiles(sourceFile);
        CleanupDatabaseFiles();
    }

    private static void CleanupSourceFiles(string fileName)
    {
        var binDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var filePath = Path.Combine(binDirectory, fileName);

        if (!File.Exists(filePath)) return;

        try
        {
            File.Delete(filePath);
            Console.WriteLine($"Successfully deleted {Path.GetFileName(filePath)}");
        }
        catch (IOException)
        {
            Console.WriteLine($"Warning: Could not delete {filePath}");
        }
    }

    private static void CleanupDatabaseFiles()
    {
        var projectDir = GetProjectDirectory();
        if (projectDir == null) return;

        SqliteConnection.ClearAllPools();

        var dbFiles = Directory.GetFiles(projectDir, "NBomber.Data.*.db");
        foreach (var dbFile in dbFiles)
        {
            try
            {
                SqliteConnection.ClearAllPools();
                File.Delete(dbFile);
                Console.WriteLine($"Successfully deleted {Path.GetFileName(dbFile)}");
            }
            catch (IOException)
            {
                Console.WriteLine($"Warning: Could not delete {dbFile}");
            }
        }
    }

    private static string? GetProjectDirectory()
    {
        return Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory)?
            .Parent?.Parent?.Parent?.FullName;
    }

    private static long GetCsvRowCount(string fileName)
    {
        var binDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var filePath = Path.Combine(binDirectory, fileName);

        if (!File.Exists(filePath)) return 0;

        long count = 0;
        using var reader = new StreamReader(filePath);

        // Skip header line
        reader.ReadLine();

        while (reader.ReadLine() != null)
        {
            count++;
        }

        return count;
    }

    private static void ForceGarbageCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}

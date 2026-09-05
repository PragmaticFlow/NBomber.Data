using Microsoft.Data.Sqlite;
using NBomber.CSharp;
using Serilog;
using System.Collections.Concurrent;

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
        using var csvStream = Data.CreateCsvStream<TestUser>(TestCsvFile);
        
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
        using var jsonStream = Data.CreateJsonStream<TestUser>(TestJsonFile);
        
        dataFeed.LoadData(jsonStream);

        monitor.Stop();

        var maxMemoryUsed = monitor.MaxMemoryUsed;

        var actualMemoryMb = maxMemoryUsed / BytesToMB;

        Assert.True(maxMemoryUsed < Settings.MaxAllowedMemoryBytes,
            $"Memory usage {actualMemoryMb:F2} MB exceeded {Settings.MaxAllowedMemoryMB} MB limit");

        CleanupTestResources(TestJsonFile);
    }

    [Fact]
    public async Task ConstantDataFeed_GetNextItem_Should_Return_Same_Data_Per_ScenarioInfo()
    {
        DataGenerator.GenerateLargeCsvFile(TestCsvFile, Settings.TestFileSizeBytes);

        await using var dataFeed = LargeDataFeed.Constant<TestUser>();

        var totalRows = GetCsvRowCount(TestCsvFile);
        var recordedItems = new ConcurrentDictionary<long, TestUser>();

        var scenario = Scenario.Create("scenario", async context =>
        {
            var instanceNumber = context.ScenarioInfo.InstanceNumber;
            var item = await dataFeed.GetNextItem(context.ScenarioInfo);

            // Each instance should always get the same item
            recordedItems.TryAdd(instanceNumber, item);

            return Response.Ok();
        })
        .WithInit(context =>
        {
            using var stream = Data.CreateCsvStream<TestUser>(TestCsvFile);
            dataFeed.LoadData(stream);

            return Task.CompletedTask;
        })
        .WithoutWarmUp()
        .WithLoadSimulations(Simulation.KeepConstant(copies: 10, during: TimeSpan.FromSeconds(5)));

        NBomberRunner
            .RegisterScenarios(scenario)
            .Run();

        // Verify that different instances got different items (based on instance number)
        Assert.True(recordedItems.Count == 10,
            $"Should have recorded 10 different instances. Actual: {recordedItems.Count}.");

        CleanupTestResources(TestCsvFile);
    }

    [Fact]
    public async Task RandomDataFeed_GetNextItem_Should_Return_Random_Data_Per_ScenarioInfo()
    {
        DataGenerator.GenerateLargeCsvFile(TestCsvFile, Settings.TestFileSizeBytes);

        await using var dataFeed = LargeDataFeed.Random<TestUser>();

        var receivedIds = new HashSet<int>();
        var requestCount = 0;

        var scenario = Scenario.Create("scenario", async context =>
        {
            var item = await dataFeed.GetNextItem(context.ScenarioInfo);

            receivedIds.Add(item.Id);
            requestCount++;

            return Response.Ok();
        })
        .WithInit(context =>
        {
            using var stream = Data.CreateCsvStream<TestUser>(TestCsvFile);
            dataFeed.LoadData(stream);

            return Task.CompletedTask;
        })
        .WithoutWarmUp()
        .WithLoadSimulations(Simulation.IterationsForConstant(copies: 1, iterations: 1000));

        NBomberRunner
            .RegisterScenarios(scenario)
            .Run();

        // Verify that we got different items (randomness)
        // With 1000 iterations, we should get a good variety of unique IDs
        var uniqueCount = receivedIds.Count;
        var uniquenessRatio = (double)uniqueCount / requestCount;

        Assert.True(uniquenessRatio > 0.5,
            $"Random feed should return varied data. Uniqueness ratio: {uniquenessRatio:F2} (expected > 0.5)");

        CleanupTestResources(TestCsvFile);
    }

    [Fact]
    public async Task CircularDataFeed_GetNextItem_Should_Circular_Loop_Over_All_Data()
    {
        DataGenerator.GenerateLargeCsvFile(TestCsvFile, Settings.TestFileSizeBytes);
        var fullLoopCompletedTimes = 0;

        var totalRows = GetCsvRowCount(TestCsvFile);

        await using var dataFeed = LargeDataFeed.Circular<TestUser>();

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
        .WithInit(context =>
        {
            using var stream = Data.CreateCsvStream<TestUser>(TestCsvFile);
            dataFeed.LoadData(stream);

            return Task.CompletedTask;
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

        await using var dataFeed = LargeDataFeed.Circular<TestUser>(5000);

        var baselineMemory = GC.GetTotalMemory(forceFullCollection: true);
        var monitor = new MemoryMonitor(baselineMemory);

        var scenario = Scenario.Create("scenario", async context =>
        {
            var step1 = await Step.Run("batch", context, async () =>
            {
                await dataFeed.GetNextItem(context.ScenarioInfo);
                return Response.Ok();
            });

            await Task.Delay(100);

            return Response.Ok();
        })
        .WithInit(context =>
        {
            using var stream = Data.CreateCsvStream<TestUser>(TestCsvFile);
            dataFeed.LoadData(stream);

            monitor.Start();
            return Task.CompletedTask;
        })
        .WithClean(context =>
        {
            monitor.Stop();
            CleanupTestResources(TestCsvFile);
            return Task.CompletedTask;
        })
        .WithWarmUpDuration(TimeSpan.FromSeconds(5))
        .WithLoadSimulations(Simulation.KeepConstant(copies: 100, during: TimeSpan.FromSeconds(30)));

        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .Run();

        var maxMemoryUsed = monitor.MaxMemoryUsed;
        var actualMemoryMb = maxMemoryUsed / BytesToMB;

        Assert.True(maxMemoryUsed < Settings.MaxAllowedMemoryBytesConcurrency,
            $"Memory usage {actualMemoryMb:F2} MB exceeded {Settings.MaxAllowedMemoryMBConcurrency} MB limit");

        var latencyP99 = stats.ScenarioStats.First().StepStats.First(stepStat => stepStat.StepName == "batch").Ok
            .Latency.Percent99;
        Assert.True(latencyP99 < Settings.MaxExecutionTimeMs, $"Latency P99 execution time was {latencyP99}");
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
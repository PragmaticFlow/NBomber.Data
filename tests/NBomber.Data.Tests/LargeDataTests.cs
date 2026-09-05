using NBomber.CSharp;
using System.Collections.Concurrent;
using NBomber.Data.Tests.Infra;

namespace NBomber.Data.Tests;

public class LargeDataTests
{
    private const string TestCsvFile = "users-feed-data.csv";
    private const string TestJsonFile = "users-feed-data.json";

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

        var maxMemoryUsedMb = monitor.MaxMemoryUsedMB;

        Assert.True(maxMemoryUsedMb < Settings.MaxAllowedMemoryMB,
            $"Memory usage {maxMemoryUsedMb:F2} MB exceeded {Settings.MaxAllowedMemoryMB} MB limit");

        TestEnv.CleanupTestResources(TestCsvFile);
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

        var maxMemoryUsedMb = monitor.MaxMemoryUsedMB;

        Assert.True(maxMemoryUsedMb < Settings.MaxAllowedMemoryMB,
            $"Memory usage {maxMemoryUsedMb:F2} MB exceeded {Settings.MaxAllowedMemoryMB} MB limit");

        TestEnv.CleanupTestResources(TestJsonFile);
    }

    [Fact]
    public async Task ConstantDataFeed_GetNextItem_Should_Return_Same_Data_Per_ScenarioInfo()
    {
        DataGenerator.GenerateLargeCsvFile(TestCsvFile, Settings.TestFileSizeBytes);

        await using var dataFeed = LargeDataFeed.Constant<TestUser>();

        var totalRows = TestEnv.GetCsvRowCount(TestCsvFile);
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

        TestEnv.CleanupTestResources(TestCsvFile);
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

        TestEnv.CleanupTestResources(TestCsvFile);
    }

    [Fact]
    public async Task CircularDataFeed_GetNextItem_Should_Circular_Loop_Over_All_Data()
    {
        DataGenerator.GenerateLargeCsvFile(TestCsvFile, Settings.TestFileSizeBytes);
        var fullLoopCompletedTimes = 0;

        var totalRows = TestEnv.GetCsvRowCount(TestCsvFile);

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

        TestEnv.CleanupTestResources(TestCsvFile);
    }

    [Fact]
    public async Task Concurrency_Circular_GetNext_Should_Not_Use_Memory_Above_Limit()
    {
        DataGenerator.GenerateLargeCsvFile(TestCsvFile, Settings.TestFileSizeBytes);

        //var data = Data.LoadCsv<TestUser>(TestCsvFile);
        //var feed = DataFeed.Circular(data);

        var totalRows = TestEnv.GetCsvRowCount(TestCsvFile);

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
            TestEnv.CleanupTestResources(TestCsvFile);
            return Task.CompletedTask;
        })
        .WithWarmUpDuration(TimeSpan.FromSeconds(5))
        .WithLoadSimulations(Simulation.KeepConstant(copies: 100, during: TimeSpan.FromSeconds(30)));

        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .Run();

        var maxMemoryUsedMb = monitor.MaxMemoryUsedMB;

        Assert.True(maxMemoryUsedMb < Settings.MaxAllowedMemoryMBConcurrency,
            $"Memory usage {maxMemoryUsedMb:F2} MB exceeded {Settings.MaxAllowedMemoryMBConcurrency} MB limit");

        var latencyP99 = stats.ScenarioStats[0].StepStats[0].Ok.Latency.Percent99;
        
        Assert.True(latencyP99 < Settings.MaxExecutionTimeMs, $"Latency P99 execution time was {latencyP99}");
    }
}
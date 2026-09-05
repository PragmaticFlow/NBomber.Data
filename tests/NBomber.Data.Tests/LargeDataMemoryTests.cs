using NBomber.CSharp;
using NBomber.Data.Tests.Infra;

namespace NBomber.Data.Tests;

[Collection(LargeDataCollection.Name)]
public class LargeDataMemoryTests
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
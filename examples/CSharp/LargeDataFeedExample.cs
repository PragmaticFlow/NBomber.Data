using NBomber.CSharp;
using NBomber.Data;

public static class LargeDataFeedExample
{
    public static async Task Run()
    {
        await using var dataFeed = LargeDataFeed.Constant<User>();
        //await using var dataFeed = LargeDataFeed.Random<User>();
        //await using var dataFeed = LargeDataFeed.Circular<User>();

        var scenario = Scenario.Create("large_data_feed_scenario", async context =>
        {
            var item = await dataFeed.GetNextItem(context.ScenarioInfo);

            return Response.Ok();
        })
        .WithInit(context =>
        {
            using var stream = Data.CreateJsonStream<User>("users-feed-data.json");
            dataFeed.LoadData(stream);
            return Task.CompletedTask;
        })
        .WithoutWarmUp()
        .WithLoadSimulations(Simulation.KeepConstant(copies: 2, during: TimeSpan.FromSeconds(30)));

        NBomberRunner
            .RegisterScenarios(scenario)
            .Run();
    }
}

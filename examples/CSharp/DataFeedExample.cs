using NBomber.CSharp;
using NBomber.Data;

public static class DataFeedExample
{
    public static void Run()
    {
        // Load by file
        var data = Data.LoadCsv<User>("users-feed-data.csv");
        //var data = Data.LoadJson<User[]>("users-feed-data.json");

        // Load by URL
        //var data = Data.LoadCsv<User>("https://raw.githubusercontent.com/PragmaticFlow/NBomber/e54c45912b1826f54376a8668da556aeb922b9d6/examples/CSharpProd/DataFeed/users-feed-data.csv");
        //var data = Data.LoadJson<User[]>("https://raw.githubusercontent.com/PragmaticFlow/NBomber/e54c45912b1826f54376a8668da556aeb922b9d6/examples/CSharpProd/DataFeed/users-feed-data.json");

        var feed = DataFeed.Constant(data);
        //var feed = DataFeed.Random(data);
        //var feed = DataFeed.Circular(data);

        var scenario = Scenario.Create("data_feed_scenario", async context =>
        {
            var item = feed.GetNextItem(context.ScenarioInfo);

            return Response.Ok();
        })
        .WithoutWarmUp()
        .WithLoadSimulations(Simulation.KeepConstant(copies: 2, during: TimeSpan.FromSeconds(30)));

        NBomberRunner
            .RegisterScenarios(scenario)
            .Run();
    }
}

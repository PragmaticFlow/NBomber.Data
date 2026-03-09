using MessagePack;
using NBomber.CSharp;
using NBomber.LargeData;

//var data = new[] {1, 2, 3, 4, 5};

// Load by file
//var data = Data.LoadCsv<User>("users-feed-data.csv");
//var data = Data.LoadJson<User[]>("users-feed-data.json");

// Load by URL
//var data = Data.LoadCsv<User>("https://raw.githubusercontent.com/PragmaticFlow/NBomber/e54c45912b1826f54376a8668da556aeb922b9d6/examples/CSharpProd/DataFeed/users-feed-data.csv");
//var data = Data.LoadJson<User[]>("https://raw.githubusercontent.com/PragmaticFlow/NBomber/e54c45912b1826f54376a8668da556aeb922b9d6/examples/CSharpProd/DataFeed/users-feed-data.json");

//var feed = DataFeed.Constant(data);
//var feed = DataFeed.Random(data);
//var feed = DataFeed.Circular(data);

await using var dataFeed = LargeDataFeed.Constant<User>();

var scenario = Scenario.Create("scenario", async context =>
{
    var item = await dataFeed.GetNextItem(context.ScenarioInfo);

    return Response.Ok();
})
.WithInit(context =>
{
    using var stream = LargeData.OpenJsonStream<User>("users-feed-data.json");
    dataFeed.LoadData(context.Logger, stream);
    return Task.CompletedTask;
})
.WithoutWarmUp()
.WithLoadSimulations(Simulation.KeepConstant(copies: 2, during: TimeSpan.FromSeconds(30)));

NBomberRunner
    .RegisterScenarios(scenario)
    .Run();

[MessagePackObject]
public class User
{
    [Key(0)]
    public int Id { get; set; }

    [Key(1)]
    public string Name { get; set; }
}
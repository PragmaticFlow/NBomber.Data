using NBomber.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NBomber.LargeData;

/// <summary>
/// Represents an async data source for feeding large test data into a load test scenario.
/// Designed for datasets that are too large to fit in memory.
/// </summary>
/// <typeparam name="T">The type of data items provided by the feed.</typeparam>
public interface IAsyncDataFeed<T> : IAsyncDisposable
{
    /// <summary>
    /// Retrieves the next data item from the feed based on the provided scenario context.
    /// </summary>
    /// <param name="scenarioInfo">Contains contextual information about the executing scenario.</param>
    /// <returns>A single data item of type <typeparamref name="T"/> to be used in the scenario.</returns>
    ValueTask<T> GetNextItem(ScenarioInfo scenarioInfo);

    /// <summary>
    /// Loads data from the provided enumerable into the feed.
    /// Must be called before GetNextItem.
    /// </summary>
    /// <param name="logger">The logger for diagnostic output.</param>
    /// <param name="data">The data to load into the feed.</param>
    void LoadData(Serilog.ILogger logger, IEnumerable<T> data);
}

/// <summary>
/// LargeDataFeed helps inject large test data into your load test. It represents a data source.
/// Designed for datasets that are too large to fit in memory.
/// </summary>
public static class LargeDataFeed
{
    /// <summary>
    /// Creates DataFeed that picks constant value per Scenario copy.
    /// Every Scenario copy will have unique constant value.
    /// </summary>
    /// <param name="elementsInMemoryCount">Number of items to keep in memory.</param>
    public static IAsyncDataFeed<T> Constant<T>(int elementsInMemoryCount = 1000) =>
        new ConstantLargeDataFeed<T>(elementsInMemoryCount);

    /// <summary>
    /// Creates DataFeed that randomly picks an item per GetNextItem() invocation.
    /// </summary>
    /// <param name="elementsInMemoryCount">Number of items to keep in memory.</param>
    public static IAsyncDataFeed<T> Random<T>(int elementsInMemoryCount = 1000) =>
        new RandomLargeDataFeed<T>(elementsInMemoryCount);

    /// <summary>
    /// Creates DataFeed that goes back to the top of the sequence once the end is reached.
    /// </summary>
    /// <param name="elementsInMemoryCount">Number of items to keep in memory.</param>
    public static IAsyncDataFeed<T> Circular<T>(int elementsInMemoryCount = 1000) =>
        new CircularLargeDataFeed<T>(elementsInMemoryCount);
}

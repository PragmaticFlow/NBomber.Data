using NBomber.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NBomber.Data;

/// <summary>
/// Represents a data source for feeding test data into a load test scenario.
/// </summary>
/// <typeparam name="T">The type of data items provided by the feed.</typeparam>
public interface IDataFeed<out T>
{
    /// <summary>
    /// Gets the full collection of data items available in the feed.
    /// </summary>
    T[] Items { get; }

    /// <summary>
    /// Retrieves the next data item from the feed based on the provided scenario context.
    /// </summary>
    /// <param name="scenarioInfo">
    /// Contains contextual information about the executing scenario, which may influence data selection logic.
    /// </param>
    /// <returns>A single data item of type <typeparamref name="T"/> to be used in the scenario.</returns>
    T GetNextItem(ScenarioInfo scenarioInfo);
}

/// <summary>
/// DataFeed helps inject test data into your load test. It represents a data source.
/// </summary>
public static class DataFeed
{
    /// <summary>
    /// Creates DataFeed that picks constant value per Scenario copy.
    /// Every Scenario copy will have unique constant value.
    /// </summary>
    public static IDataFeed<T> Constant<T>(IEnumerable<T> data) =>
        new ConstantDataFeed<T>(data);

    /// <summary>
    /// Creates DataFeed that goes back to the top of the sequence once the end is reached.
    /// </summary>
    public static IDataFeed<T> Circular<T>(IEnumerable<T> data) =>
        new CircularDataFeed<T>(data);

    /// <summary>
    /// Creates DataFeed that randomly picks an item per GetNextItem() invocation.
    /// </summary>
    public static IDataFeed<T> Random<T>(IEnumerable<T> data) =>
        new RandomDataFeed<T>(data);
}

internal class ConstantDataFeed<T> : IDataFeed<T>
{
    public T[] Items { get; }

    public ConstantDataFeed(IEnumerable<T> data)
    {
        Items = data.ToArray();
    }

    public T GetNextItem(ScenarioInfo scenarioInfo)
    {
        var index = scenarioInfo.InstanceNumber % Items.Length;
        return Items[index];
    }
}

internal class CircularDataFeed<T> : IDataFeed<T>
{
    private readonly object _lock = new();
    private readonly IEnumerator<T> _enumerator;

    public T[] Items { get; }

    public CircularDataFeed(IEnumerable<T> data)
    {
        Items = data.ToArray();
        _enumerator = CreateInfiniteStream(Items).GetEnumerator();
    }

    private static IEnumerable<T> CreateInfiniteStream(T[] items)
    {
        while (true)
        {
            foreach (var item in items)
                yield return item;
        }
    }

    public T GetNextItem(ScenarioInfo scenarioInfo)
    {
        lock (_lock)
        {
            _enumerator.MoveNext();
            return _enumerator.Current;
        }
    }
}

internal class RandomDataFeed<T> : IDataFeed<T>
{
    private static readonly Random _random = new();

    public T[] Items { get; }

    public RandomDataFeed(IEnumerable<T> data)
    {
        Items = data.ToArray();
    }

    public T GetNextItem(ScenarioInfo scenarioInfo)
    {
        var index = _random.Next(Items.Length);
        return Items[index];
    }
}
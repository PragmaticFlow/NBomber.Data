using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;

namespace NBomber.Data;

/// <summary>
/// Represents a streaming JSON data source for processing large JSON files.
/// Designed for datasets that are too large to fit in memory.
/// Items are deserialized on-demand as you iterate through the stream.
/// </summary>
/// <typeparam name="T">The type each JSON element is deserialized into.</typeparam>
public class JsonStream<T> : IEnumerable<T>, IDisposable
{
    private readonly Stream _stream;

    internal JsonStream(Stream stream)
    {
        _stream = stream;
    }

    public IEnumerator<T> GetEnumerator()
    {
        return JsonSerializer.DeserializeAsyncEnumerable<T>(_stream)
                             .ToEnumerable()
                             .Where(x => x != null)
                             .GetEnumerator()!;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void Dispose()
    {
        _stream?.Dispose();
    }
}

/// <summary>
/// Represents a streaming CSV data source for processing large CSV files.
/// Designed for datasets that are too large to fit in memory.
/// Rows are parsed on-demand as you iterate through the stream.
/// </summary>
/// <typeparam name="T">The type each CSV row is mapped into.</typeparam>
public class CsvStream<T> : IEnumerable<T>, IDisposable
{
    private readonly Stream _stream;

    internal CsvStream(Stream stream)
    {
        _stream = stream;
    }

    public IEnumerator<T> GetEnumerator()
    {
        var reader = new StreamReader(_stream);
        var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture));
        return csv.GetRecords<T>().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void Dispose()
    {
        _stream?.Dispose();
    }
}

/// <summary>
/// Provides utility functions for streaming large data files.
/// Designed for datasets that are too large to fit in memory.
/// </summary>
public static class LargeData
{
    /// <summary>
    /// Opens a streaming JSON data source from a file.
    /// Items are deserialized on-demand without loading the entire file into memory.
    /// </summary>
    /// <param name="path">The path to the JSON file.</param>
    /// <typeparam name="T">The type each JSON element is deserialized into.</typeparam>
    /// <returns>A <see cref="JsonStream{T}"/> that streams data from the file.</returns>
    public static JsonStream<T> OpenJsonStream<T>(string path)
    {
        var stream = Uri.IsWellFormedUriString(path, UriKind.Absolute)
            ? new HttpClient().GetStreamAsync(path).GetAwaiter().GetResult()
            : File.OpenRead(path);

        return new JsonStream<T>(stream);
    }

    /// <summary>
    /// Opens a streaming CSV data source from a file.
    /// Rows are parsed on-demand without loading the entire file into memory.
    /// </summary>
    /// <param name="path">The path to the CSV file.</param>
    /// <typeparam name="T">The type each CSV row is mapped into.</typeparam>
    /// <returns>A <see cref="CsvStream{T}"/> that streams data from the file.</returns>
    public static CsvStream<T> OpenCsvStream<T>(string path)
    {
        var stream = Uri.IsWellFormedUriString(path, UriKind.Absolute)
            ? new HttpClient().GetStreamAsync(path).GetAwaiter().GetResult()
            : File.OpenRead(path);

        return new CsvStream<T>(stream);
    }
}

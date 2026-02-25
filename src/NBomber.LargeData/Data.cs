using System.Collections;
using System.Globalization;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;

namespace NBomber.LargeData;

// concurrency scenario: copies 100, during 1 minute, circular js.totalMemory + execution time
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
                             .ToBlockingEnumerable()
                             .GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void Dispose()
    {
        _stream?.Dispose();
    }
}

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

public static class LargeData
{
    /// <summary>
    /// Opens a typed JSON stream from a JSON file or URL without loading all data into memory.
    /// </summary>
    public static JsonStream<T> OpenJsonStream<T>(string path)
    {
        var stream = Uri.IsWellFormedUriString(path, UriKind.Absolute)
            ? new HttpClient().GetStreamAsync(path).GetAwaiter().GetResult()
            : File.OpenRead(path);

        return new JsonStream<T>(stream);
    }

    /// <summary>
    /// Opens a typed CSV stream from a CSV file or URL without loading all data into memory.
    /// </summary>
    public static CsvStream<T> OpenCsvStream<T>(string path)
    {
        var stream = Uri.IsWellFormedUriString(path, UriKind.Absolute)
            ? new HttpClient().GetStreamAsync(path).GetAwaiter().GetResult()
            : File.OpenRead(path);

        return new CsvStream<T>(stream);
    }
}

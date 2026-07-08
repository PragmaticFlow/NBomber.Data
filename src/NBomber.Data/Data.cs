using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using CsvHelper;

namespace NBomber.Data;

/// <summary>
/// Provides utility functions for generating random data and loading structured data from JSON or CSV sources.
/// Supports both local file paths and remote HTTP URLs.
/// </summary>
public static class Data
{
    [ThreadStatic]
    private static Random? _random;
    private static readonly HttpClient HttpClient = new();
    private static Random RandomInstance => _random ??= new Random();

    /// <summary>
    /// Generates a random byte array of the specified size.
    /// </summary>
    /// <param name="sizeInBytes">The number of bytes to generate.</param>
    /// <returns>An array of random bytes.</returns>
    public static byte[] GenerateRandomBytes(int sizeInBytes)
    {
        var buffer = new byte[sizeInBytes];
        RandomInstance.NextBytes(buffer);
        return buffer;
    }

    /// <summary>
    /// Loads and deserializes a JSON document from a local file or an HTTP URL into a value of type <typeparamref name="T"/>.
    /// </summary>
    /// <param name="path">The full path to the local JSON file or a valid HTTP/HTTPS URL.</param>
    /// <typeparam name="T">The target type into which the JSON will be deserialized.</typeparam>
    /// <returns>An instance of <typeparamref name="T"/> populated with the deserialized JSON data.</returns>
    public static T? LoadJson<T>(string path)
    {
        using var stream = GetStream(path);
        return JsonSerializer.Deserialize<T>(stream);
    }

    /// <summary>
    /// Loads and parses a CSV file from a local file or an HTTP URL into an array of items of type <typeparamref name="T"/>.
    /// </summary>
    /// <param name="path">The full path to the local CSV file or a valid HTTP/HTTPS URL.</param>
    /// <typeparam name="T">The type each row in the CSV is mapped to.</typeparam>
    /// <returns>An array of items of type <typeparamref name="T"/> parsed from the CSV data.</returns>
    public static T[] LoadCsv<T>(string path)
    {
        using var stream = GetStream(path);
        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        return csv.GetRecords<T>().ToArray();
    }

    private static Stream GetStream(string path)
    {
        if (Uri.IsWellFormedUriString(path, UriKind.Absolute))
        {
            return HttpClient.GetStreamAsync(path).GetAwaiter().GetResult();
        }

        return File.OpenRead(path);
    }
}

namespace NBomber.Data

open System
open System.Globalization
open System.IO
open System.Net.Http
open System.Text.Json
open CsvHelper

/// Provides utility functions for generating random data and loading structured data from JSON or CSV sources.
/// Supports both local file paths and remote HTTP URLs.
type Data =

    /// <summary>
    /// Generates a random byte array of the specified size.
    /// </summary>
    /// <param name="sizeInBytes">The number of bytes to generate.</param>
    /// <returns>An array of random bytes.</returns>
    [<CompiledName("GenerateRandomBytes")>]
    static member generateRandomBytes (sizeInBytes: int) =
        let buffer = Array.zeroCreate<byte> sizeInBytes
        Random().NextBytes buffer
        buffer
    
    /// <summary>
    /// Loads and deserializes a JSON document from a local file or an HTTP URL into a value of type <typeparamref name="'T"/>.
    /// </summary>
    /// <param name="path">
    /// The full path to the local JSON file or a valid HTTP/HTTPS URL.
    /// </param>
    /// <typeparam name="T">The target type into which the JSON will be deserialized.</typeparam>
    /// <returns>An instance of <typeparamref name="T"/> populated with the deserialized JSON data.</returns>
    [<CompiledName("LoadJson")>]
    static member loadJson<'T> (path: string) =        
        use stream =            
            if Uri.IsWellFormedUriString(path, UriKind.Absolute) then
                use client = new HttpClient()
                client.GetStreamAsync(path).GetAwaiter().GetResult()
            else
                File.OpenRead path
                
        JsonSerializer.Deserialize<'T>(stream)
        
    
    /// <summary>
    /// Loads and parses a CSV file from a local file or an HTTP URL into an array of items of type <typeparamref name="T"/>.
    /// </summary>
    /// <param name="path">
    /// The full path to the local CSV file or a valid HTTP/HTTPS URL.
    /// </param>
    /// <typeparam name="T">The type each row in the CSV is mapped to.</typeparam>
    /// <returns>An array items of type <typeparamref name="T"/> parsed from the CSV data.</returns>
    [<CompiledName("LoadCsv")>]
    static member loadCsv<'T> (path: string) =
        use stream =            
            if Uri.IsWellFormedUriString(path, UriKind.Absolute) then
                use client = new HttpClient()
                new StreamReader(client.GetStreamAsync(path).GetAwaiter().GetResult())
            else
                new StreamReader(File.OpenRead path)       
        
        use csv = new CsvReader(stream, CultureInfo.InvariantCulture)
        csv.GetRecords<'T>()
        |> Seq.toArray
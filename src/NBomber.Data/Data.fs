namespace NBomber.Data

open System
open System.Globalization
open System.IO
open System.Net.Http
open System.Text.Json
open CsvHelper

type Data =

    [<CompiledName("GenerateRandomBytes")>]
    static member generateRandomBytes (sizeInBytes: int) =
        let buffer = Array.zeroCreate<byte> sizeInBytes
        Random().NextBytes buffer
        buffer
    
    /// Loads JSON file by full file path or by HTTP URL.
    [<CompiledName("LoadJson")>]
    static member loadJson<'T> (path: string) =        
        use stream =            
            if Uri.IsWellFormedUriString(path, UriKind.Absolute) then
                use client = new HttpClient()
                client.GetStreamAsync(path).GetAwaiter().GetResult()
            else
                File.OpenRead path
                
        JsonSerializer.Deserialize<'T>(stream)
        
    
    /// Loads CSV file by full file path or by HTTP URL.
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
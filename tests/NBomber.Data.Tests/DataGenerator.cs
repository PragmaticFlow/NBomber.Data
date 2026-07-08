using Bogus;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using System.Text.Json;

//DataGenerator.GenerateLargeCsvFile("users-feed-data.csv", 1024 * 1024 * 1024);
//DataGenerator.GenerateLargeJsonFile("users-feed-data.json", 1024 * 1024 * 1024);

internal static class DataGenerator
{
    public static void GenerateLargeJsonFile(string filePath, long targetSizeInBytes)
    {
        var faker = new Faker<TestUser>()
            .RuleFor(u => u.Id, f => f.IndexFaker)
            .RuleFor(u => u.Name, f => f.Name.FullName());

        using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        using var writer = new Utf8JsonWriter(fileStream, new JsonWriterOptions { Indented = false });

        writer.WriteStartArray();

        var fileInfo = new FileInfo(filePath);
        var id = 1;
        var lastReportedSize = 0L;
        const long reportInterval = 10 * 1024 * 1024; // 10MB

        while (true)
        {
            var user = faker.Generate();
            user.Id = id++;

            JsonSerializer.Serialize(writer, user);

            if (id % 10000 == 0)
            {
                writer.Flush();
                fileStream.Flush();

                fileInfo.Refresh();
                var currentSize = fileInfo.Length;

                if (currentSize - lastReportedSize >= reportInterval)
                {
                    Console.WriteLine($"Generated {id:N0} records, File size: {currentSize / 1024 / 1024:N2} MB");
                    lastReportedSize = currentSize;
                }

                if (currentSize >= targetSizeInBytes)
                {
                    break;
                }
            }
        }

        writer.WriteEndArray();
        writer.Flush();

        Console.WriteLine($"Completed! Total records: {id:N0}, Final size: {fileInfo.Length / 1024 / 1024:N2} MB");
    }

    public static void GenerateLargeCsvFile(string filePath, long targetSizeInBytes)
    {
        var faker = new Faker<TestUser>()
            .RuleFor(u => u.Id, f => f.IndexFaker)
            .RuleFor(u => u.Name, f => f.Name.FullName());

        using var writer = new StreamWriter(filePath);
        using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture));

        csv.WriteHeader<TestUser>();
        csv.NextRecord();

        var fileInfo = new FileInfo(filePath);
        var id = 1;
        var lastReportedSize = 0L;
        const long reportInterval = 10 * 1024 * 1024; // 10MB

        while (true)
        {
            var user = faker.Generate();
            user.Id = id++;

            csv.WriteRecord(user);
            csv.NextRecord();

            if (id % 10000 == 0)
            {
                csv.Flush();
                writer.Flush();

                fileInfo.Refresh();
                var currentSize = fileInfo.Length;

                if (currentSize - lastReportedSize >= reportInterval)
                {
                    Console.WriteLine($"Generated {id:N0} records, File size: {currentSize / 1024 / 1024:N2} MB");
                    lastReportedSize = currentSize;
                }

                if (currentSize >= targetSizeInBytes)
                {
                    break;
                }
            }
        }

        Console.WriteLine($"Completed! Total records: {id:N0}, Final size: {fileInfo.Length / 1024 / 1024:N2} MB");
    }
}

public class TestUser
{
    public int Id { get; set; }

    public string Name { get; set; }
}

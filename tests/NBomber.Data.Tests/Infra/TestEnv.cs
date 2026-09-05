using Microsoft.Data.Sqlite;

namespace NBomber.Data.Tests.Infra;

class TestEnv
{
    public static long GetCsvRowCount(string fileName)
    {
        var binDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var filePath = Path.Combine(binDirectory, fileName);

        if (!File.Exists(filePath)) 
            return 0;

        long count = 0;
        using var reader = new StreamReader(filePath);

        // Skip header line
        reader.ReadLine();

        while (reader.ReadLine() != null)
        {
            count++;
        }

        return count;
    }
    
    public static string? GetProjectDirectory()
    {
        return Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory)?.Parent?.Parent?.Parent?.FullName;
    }
    
    public static void CleanupTestResources(string sourceFile)
    {
        TestEnv.ForceGarbageCollection();
        Thread.Sleep(500);

        CleanupSourceFiles(sourceFile);
        CleanupDatabaseFiles();
    }

    public static void CleanupSourceFiles(string fileName)
    {
        var binDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var filePath = Path.Combine(binDirectory, fileName);

        if (!File.Exists(filePath))
            return;

        try
        {
            File.Delete(filePath);
            Console.WriteLine($"Successfully deleted {Path.GetFileName(filePath)}");
        }
        catch (IOException)
        {
            Console.WriteLine($"Warning: Could not delete {filePath}");
        }
    }
    
    public static void CleanupDatabaseFiles()
    {
        var projectDir = TestEnv.GetProjectDirectory();
        if (projectDir == null) 
            return;

        SqliteConnection.ClearAllPools();

        var dbFiles = Directory.GetFiles(projectDir, "NBomber.Data.*.db");
        foreach (var dbFile in dbFiles)
        {
            try
            {
                SqliteConnection.ClearAllPools();
                File.Delete(dbFile);
                Console.WriteLine($"Successfully deleted {Path.GetFileName(dbFile)}");
            }
            catch (IOException)
            {
                Console.WriteLine($"Warning: Could not delete {dbFile}");
            }
        }
    }
    
    public static void ForceGarbageCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
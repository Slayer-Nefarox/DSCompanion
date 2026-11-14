using System.Reflection;
using System.Text;

static class CrashReporter
{
    private static readonly object SyncRoot = new();
    private static string? _crashDirectory;

    public static void Initialize()
    {
        _crashDirectory = Path.Combine(AppContext.BaseDirectory, "crash");
        Directory.CreateDirectory(_crashDirectory);
    }

    public static void Report(string context, Exception? exception = null)
    {
        try
        {
            string directory = _crashDirectory
                ?? Path.Combine(AppContext.BaseDirectory, "crash");
            Directory.CreateDirectory(directory);

            var report = new StringBuilder()
                .AppendLine("DualSense Companion crash report")
                .AppendLine($"Local time: {DateTimeOffset.Now:O}")
                .AppendLine($"UTC time: {DateTimeOffset.UtcNow:O}")
                .AppendLine($"Application version: {Assembly.GetExecutingAssembly().GetName().Version}")
                .AppendLine($"Process: {Environment.ProcessPath}")
                .AppendLine($"Operating system: {Environment.OSVersion}")
                .AppendLine($"Context: {context}");

            if (exception != null)
            {
                report.AppendLine()
                    .AppendLine("Exception:")
                    .AppendLine(exception.ToString());
            }

            string fileName = $"crash-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.txt";
            lock (SyncRoot)
            {
                File.WriteAllText(
                    Path.Combine(directory, fileName),
                    report.ToString(),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }
        catch (Exception loggingException)
        {
            Console.Error.WriteLine(
                $"Failed to write crash report for '{context}': {loggingException}");
        }
    }
}

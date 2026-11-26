using System.Reflection;
using System.Text;

// Centraliza a criação de arquivos locais com informações de falhas da aplicação.
static class CrashReporter
{
    // Garante que escritas simultâneas não gravem informações no mesmo arquivo.
    private static readonly object SyncRoot = new();
    // Diretório preparado na inicialização; pode ser recalculado se ainda não estiver definido.
    private static string? _crashDirectory;

    // Prepara o diretório usado pelos relatórios de falhas.
    public static void Initialize()
    {
        // Prepara o diretório único usado por todos os relatórios futuros.
        // Usa uma pasta "crash" dentro da pasta de execução da aplicação.
        _crashDirectory = Path.Combine(AppContext.BaseDirectory, "crash");
        Directory.CreateDirectory(_crashDirectory);
    }

    // Grava uma descrição da falha em um arquivo único e limitado a uma operação de escrita.
    public static void Report(string context, Exception? exception = null)
    {
        // Nunca deixa uma falha ao registrar outra falha impedir a execução da aplicação.
        try
        {
            // Usa o diretório preparado ou calcula novamente como alternativa segura.
            string directory = _crashDirectory
                ?? Path.Combine(AppContext.BaseDirectory, "crash");
            Directory.CreateDirectory(directory);

            // Monta o relatório em texto com horário, processo, sistema e contexto.
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
                // Inclui a pilha completa da exceção quando ela foi fornecida.
                report.AppendLine()
                    .AppendLine("Exception:")
                    .AppendLine(exception.ToString());
            }

            // Gera um nome exclusivo combinando data, hora e GUID.
            string fileName = $"crash-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.txt";
            lock (SyncRoot)
            {
                // Grava o relatório como UTF-8 sem BOM.
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

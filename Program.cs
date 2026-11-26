
// Define o ponto de entrada da aplicação e configura os tratamentos globais de exceções.
class Program
{
    // Indica ao Windows Forms que a thread principal deve usar o modelo STA.
    [STAThread]
    static void Main(string[] args)
    {
        try
        {
            // Cria a pasta e prepara o serviço responsável por registrar falhas.
            CrashReporter.Initialize();
        }
        catch (Exception ex)
        {
            // Mostra uma mensagem quando não é possível criar o diretório de relatórios.
            MessageBox.Show(
                $"The crash report folder could not be created:\n{ex.Message}",
                "DualSense Companion",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        // Encaminha exceções não tratadas por domínio da aplicação para o relatório.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            CrashReporter.Report(
                "Unhandled application-domain exception",
                e.ExceptionObject as Exception);
        // Encaminha exceções que acontecem em threads do Windows Forms.
        Application.ThreadException += (_, e) =>
            CrashReporter.Report("Unhandled Windows Forms thread exception", e.Exception);
        // Marca exceções assíncronas observadas após gerar o relatório.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashReporter.Report("Unobserved task exception", e.Exception);
            e.SetObserved();
        };

        // Verifica e, quando necessário, instala ViGEm e HidHide.
        DependencyManager.CheckAndInstallDependencies();
        // Inicializa as configurações padrão da aplicação Windows Forms.
        ApplicationConfiguration.Initialize();
        // Faz com que exceções de UI sejam capturadas pelo manipulador registrado.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        // Abre a janela principal e inicia o loop de mensagens.
        Application.Run(new MainForm());
    }
}

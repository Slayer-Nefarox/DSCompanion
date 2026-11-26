using System.Diagnostics;
using System.Net;
using System.ServiceProcess;
using System.Windows.Forms;
using Nefarius.Drivers.HidHide;

// Verifica os componentes externos exigidos pelo controle virtual e pelo ocultamento HID.
public static class DependencyManager
{
    // Avalia ViGEm e HidHide, instala os ausentes e solicita reinício quando necessário.
    public static void CheckAndInstallDependencies()
    {
        bool vigemInstalled = IsViGEmInstalled();
        bool hidHideInstalled = IsHidHideInstalled();
        bool hidHideServiceRunning = IsHidHideServiceRunning();
        bool hidHideWasInstalled = false;

        if (!vigemInstalled)
        {
            Console.WriteLine("ViGEm Bus Driver not found. Downloading...");
            if (DownloadAndInstallViGEm())
            {
                Console.WriteLine("ViGEm Bus Driver installed successfully.");
            }
            else
            {
                Console.WriteLine("Failed to install ViGEm Bus Driver.");

                MessageBox.Show(
                    "The ViGEm Bus Driver could not be installed. The application will now close.",
                    "Dependency installation failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                Environment.Exit(0);
            }
        }

        if (!hidHideInstalled)
        {
            Console.WriteLine("HidHide not found. Downloading...");
            if (DownloadAndInstallHidHide())
            {
                Console.WriteLine("HidHide installed successfully.");
                hidHideWasInstalled = true;
            }
            else
            {
                Console.WriteLine("Failed to install HidHide.");

                MessageBox.Show(
                    "HidHide could not be installed. The application will now close.",
                    "Dependency installation failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                Environment.Exit(0);
            }
        }

        if (!hidHideServiceRunning && hidHideWasInstalled)
        {
            DialogResult result = MessageBox.Show(
                "HidHide has been installed. A restart is required for changes to take effect.\nRestart now?",
                "Restart Required",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );

            if (result == DialogResult.Yes)
            {
                RestartSystem();
            }
        }
    }

    private static bool IsViGEmInstalled()
    {
        // Função auxiliar que retorna verdadeiro somente com o driver e o serviço disponíveis.
        // Confere se o arquivo do driver ViGEmBus.sys existe no Windows.
        string driverPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "ViGEmBus.sys");
        bool driverExists = File.Exists(driverPath);

        // Também verifica se o serviço correspondente está registrado e pode ser acessado.
        try
        {
            using (ServiceController service = new ServiceController("ViGEmBus"))
            {
                bool serviceExists = service.Status != ServiceControllerStatus.Stopped;
                return driverExists && serviceExists;
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Report("Failed to query ViGEm Bus service status", ex);
            return false;
        }
    }

    // Baixa, executa e confirma a instalação do driver ViGEm Bus.
    private static bool DownloadAndInstallViGEm()
    {
        // URL fixa usada para baixar o instalador oficial da versão suportada.
        string url = "https://github.com/nefarius/ViGEmBus/releases/download/v1.22.0/ViGEmBus_1.22.0_x64_x86_arm64.exe";
        // Caminho temporário onde o instalador será salvo antes da execução.
        string installerPath = Path.Combine(Path.GetTempPath(), "ViGEmBus_1.22.0_x64_x86_arm64.exe");

        try
        {
            using (WebClient client = new WebClient())
            {
                client.DownloadFile(url, installerPath);
            }

            Process process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = installerPath,
                    UseShellExecute = true
                }
            };

            process.Start();
            process.WaitForExit();

            return IsViGEmInstalled();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error installing ViGEm Bus Driver: {ex.Message}");
            CrashReporter.Report("Failed to install ViGEm Bus Driver", ex);
            return false;
        }
    }

    // Instância usada para consultar o estado e controlar o serviço HidHide.
    private static HidHideControlService _hidHide = new HidHideControlService();
    // Consulta a biblioteca HidHide para verificar se o componente está instalado.
    private static bool IsHidHideInstalled()
    {
        return _hidHide.IsInstalled;
    }

    // Verifica se o serviço HidHide está ativo no Windows.
    private static bool IsHidHideServiceRunning()
    {
        // Tenta consultar o serviço do Windows; falhas são tratadas como serviço indisponível.
        try
        {
            ServiceController service = new ServiceController("HidHide");
            return service.Status == ServiceControllerStatus.Running;
        }
        catch (Exception ex)
        {
            CrashReporter.Report("Failed to query HidHide service status", ex);
            return false;
        }
    }

    // Baixa, executa e confirma a instalação do HidHide.
    private static bool DownloadAndInstallHidHide()
    {
        // URL fixa usada para obter o instalador oficial do HidHide.
        string url = "https://github.com/nefarius/HidHide/releases/download/v1.5.230.0/HidHide_1.5.230_x64.exe";
        // Caminho temporário usado para armazenar o pacote antes da instalação.
        string installerPath = Path.Combine(Path.GetTempPath(), "HidHide_1.5.230_x64.exe");

        try
        {
            using (WebClient client = new WebClient())
            {
                client.DownloadFile(url, installerPath);
            }

            Process process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = installerPath,
                    UseShellExecute = true
                }
            };

            process.Start();
            process.WaitForExit();

            return IsHidHideInstalled();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error installing HidHide: {ex.Message}");
            CrashReporter.Report("Failed to install HidHide", ex);
            return false;
        }
    }

    // Solicita ao Windows a reinicialização necessária após instalar o HidHide.
    private static void RestartSystem()
    {
        // Agenda uma reinicialização em cinco segundos pelo comando shutdown do Windows.
        Process.Start(new ProcessStartInfo
        {
            FileName = "shutdown",
            Arguments = "/r /t 5",
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }
}

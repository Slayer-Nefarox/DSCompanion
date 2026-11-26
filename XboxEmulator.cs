using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

// Cria e controla o controlador virtual Xbox 360 por meio da biblioteca ViGEm.
class XboxEmulator
{
    // Cliente principal da infraestrutura ViGEm usada para criar dispositivos virtuais.
    private static ViGEmClient? _client;
    // Representa o controle Xbox 360 virtual atualmente conectado.
    private static IXbox360Controller? _controller;

    // Cria o cliente ViGEm e conecta o controlador Xbox 360 virtual.
    public static void Initialize()
    {
        // Inicialização idempotente: chamadas repetidas não criam um segundo controle.
        // Evita inicializar um segundo controlador virtual se já houver um ativo.
        if (_controller != null)
        {
            return;
        }

        var client = new ViGEmClient();
        IXbox360Controller? controller = null;
        try
        {
            controller = client.CreateXbox360Controller();
            controller.FeedbackReceived += OnFeedbackReceived;
            controller.Connect();
        }
        catch
        {
            if (controller != null)
            {
                controller.FeedbackReceived -= OnFeedbackReceived;
                try
                {
                    controller.Disconnect();
                }
                catch (Exception exception)
                {
                    CrashReporter.Report("Failed to disconnect Xbox controller after initialization failure", exception);
                }
            }

            try
            {
                client.Dispose();
            }
            catch (Exception exception)
            {
                CrashReporter.Report("Failed to dispose ViGEm client after initialization failure", exception);
            }

            throw;
        }

        _client = client;
        _controller = controller
            ?? throw new InvalidOperationException("ViGEm did not create an Xbox 360 controller.");
        Console.WriteLine("Virtual Xbox 360 Controller created.");
    }

    private static void OnFeedbackReceived(object sender, Xbox360FeedbackReceivedEventArgs e)
    {
        // Repassa os motores de vibração do Xbox virtual à fonte física selecionada.
        ControllerManager.SendVibrationToController(e.LargeMotor, e.SmallMotor);
    }

    // Atualiza o estado pressionado ou solto de um botão virtual.
    public static void SetButtonState(Xbox360Button button, bool pressed)
    {
        _controller?.SetButtonState(button, pressed);
    }

    // Atualiza a posição analógica de um gatilho virtual.
    public static void SetTriggerState(Xbox360Slider trigger, byte value)
    {
        _controller?.SetSliderValue(trigger, value);
    }

    // Atualiza um eixo analógico com o intervalo sinalizado suportado pelo ViGEm.
    public static void SetAxisState(Xbox360Axis axis, short value)
    {
        _controller?.SetAxisValue(axis, value);
    }

    // Remove o feedback, desconecta o dispositivo virtual e libera o cliente ViGEm.
    public static void Shutdown()
    {
        var controller = _controller;
        var client = _client;
        _controller = null;
        _client = null;

        try
        {
            if (controller != null)
            {
                controller.FeedbackReceived -= OnFeedbackReceived;
                controller.Disconnect();
            }
        }
        finally
        {
            client?.Dispose();
        }
    }
}

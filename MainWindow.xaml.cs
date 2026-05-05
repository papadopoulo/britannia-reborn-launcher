using System.Reflection;
using System.Windows;
using System.Windows.Media;

namespace BritanniaReborn;

public partial class MainWindow : Window
{
    private readonly string _uoPath;
    private readonly ServerStatusChecker _statusChecker;
    private string _loadedUsername = "";

    public MainWindow(string uoPath)
    {
        InitializeComponent();
        _uoPath = uoPath;

        MouseLeftButtonDown += (_, _) => { try { DragMove(); } catch { } };

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        LblLauncherVersion.Text = $"Britannia Reborn Launcher v{version}";

        // Cargar settings persistidos
        var settings = LauncherSettings.Load();
        TxtUsername.Text = settings.LastUsername;
        _loadedUsername = settings.LastUsername ?? "";
        if (settings.SavePassword && !string.IsNullOrEmpty(settings.SavedPassword))
        {
            TxtPassword.Password = settings.SavedPassword;
            ChkSavePassword.IsChecked = true;
        }

        // Si el usuario cambia el username escrito, limpiamos el password guardado.
        // Sin esto, el password del último login (p.ej. Lilith) se mandaría al server
        // junto con el nuevo username (p.ej. Test) → BadPass silencioso → cuo se queda
        // colgado en "Verifying account" porque -skiploginscreen no muestra el error.
        TxtUsername.TextChanged += (_, _) =>
        {
            var current = TxtUsername.Text?.Trim() ?? "";
            if (!string.Equals(current, _loadedUsername, System.StringComparison.OrdinalIgnoreCase))
            {
                TxtPassword.Password = "";
            }
        };

        // Status del servidor (polling TCP cada 3s al puerto del shard)
        _statusChecker = new ServerStatusChecker(
            Config.ServerHost, Config.ServerPort,
            Config.StatusCheckIntervalMs, Config.StatusCheckTimeoutMs);
        _statusChecker.StatusChanged += online => Dispatcher.Invoke(() => UpdateStatus(online));
        _statusChecker.Start();
        Closed += (_, _) => _statusChecker.Dispose();

        // News y eventos (carga única al abrir)
        Loaded += async (_, _) =>
        {
            await CargarNoticiasAsync();
            await CargarEventosAsync();
        };
    }

    private void UpdateStatus(bool online)
    {
        if (online)
        {
            DotStatus.Fill = new SolidColorBrush(Color.FromRgb(0x4C, 0xD9, 0x64));
            LblStatus.Text = "Servidor online — listo para entrar";
            LblStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xFF, 0xCC));
        }
        else
        {
            DotStatus.Fill = new SolidColorBrush(Color.FromRgb(0xE0, 0x4A, 0x4A));
            LblStatus.Text = "Servidor offline — esperando que vuelva...";
            LblStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0xCC));
        }
    }

    private async System.Threading.Tasks.Task CargarNoticiasAsync()
    {
        var items = await NewsClient.CargarAsync();
        if (items.Count == 0)
        {
            LblNoticiasVacio.Visibility = Visibility.Visible;
            return;
        }
        LstNoticias.ItemsSource = items;
    }

    private async System.Threading.Tasks.Task CargarEventosAsync()
    {
        var items = await EventosClient.CargarAsync();
        if (items.Count == 0)
        {
            LblEventosVacio.Visibility = Visibility.Visible;
            return;
        }
        LstEventos.ItemsSource = items;
    }

    private void BtnPlay_Click(object sender, RoutedEventArgs e) => Launch();
    private void BtnQuit_Click(object sender, RoutedEventArgs e) => Close();

    private void Launch()
    {
        var user = TxtUsername.Text.Trim();
        var pass = TxtPassword.Password;

        if (string.IsNullOrEmpty(user))
        {
            LblError.Text = "Introduce un Account Name.";
            return;
        }

        if (!LauncherCore.ExisteClassicUo())
        {
            LblError.Text = "ClassicUO no se encuentra (carpeta cuo/ falta). Reinstala el launcher.";
            return;
        }

        // IMPORTANTE: Lanzar PRIMERO, persistir DESPUÉS.
        // LauncherCore.LanzarJuego compara el username actual con el LastUsername
        // del settings para detectar cambio de cuenta y limpiar caché del cliente
        // (lastcharacter.json). Si guardáramos antes, mi código vería el valor
        // recién guardado y nunca detectaría el cambio.
        var (ok, errorOut, proc) = LauncherCore.LanzarJuego(user, pass, _uoPath);

        // Una vez lanzado OK (o aunque falle, persistir igualmente las preferencias)
        var settings = LauncherSettings.Load();
        settings.LastUsername = user;
        settings.SavePassword = ChkSavePassword.IsChecked == true;
        settings.SavedPassword = settings.SavePassword ? pass : "";
        settings.UoPath = _uoPath;
        LauncherSettings.Save(settings);
        if (!ok)
        {
            // Mostrar error completo en MessageBox (con scroll para textos largos)
            LblError.Text = "Falló al lanzar — ver detalle en mensaje.";
            MessageBox.Show(this,
                $"No se pudo lanzar ClassicUO.\n\n{errorOut}\n\nLog completo: %APPDATA%\\BritanniaReborn\\launcher.log",
                "Britannia Reborn — Error al lanzar",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Ocultar el login mientras juega. Cuando ClassicUO termine, abrimos el
        // banner (PatcherWindow modo post-game) — al darle JUGAR vuelve al login.
        // CRITICAL: parar el status checker antes de jugar — sigue polleando aunque
        // la ventana esté oculta y cada poll TCP cuenta en el IPRateLimiter del
        // server. En partidas largas se acumulan attempts y el server termina
        // baneando la IP del propio jugador silenciosamente. Cuando vuelva el banner
        // y luego la pantalla login, se crea un MainWindow nuevo con su propio
        // checker fresh (count = 1), así no se hereda estado.
        Hide();
        try { _statusChecker.Dispose(); } catch { }
        if (proc != null)
        {
            try
            {
                proc.EnableRaisingEvents = true;
                proc.Exited += (_, _) => Dispatcher.Invoke(VolverAlBanner);
            }
            catch
            {
                // Si no podemos suscribirnos (proceso ya terminado, raro), volver ya.
                VolverAlBanner();
            }
        }
        else
        {
            VolverAlBanner();
        }
    }

    private void VolverAlBanner()
    {
        // Transición segura: durante el cambio de MainWindow, ShutdownMode=OnExplicitShutdown
        // para que cerrar este login no tumbe la app.
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var banner = new PatcherWindow(_uoPath, postGame: true);
        Application.Current.MainWindow = banner;
        banner.Show();
        banner.Activate();
        Application.Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
        Close();
    }
}

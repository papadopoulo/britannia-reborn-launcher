using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace BritanniaReborn;

public partial class App : Application
{
    private static readonly string LogDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BritanniaReborn");
    private static readonly string LogFile = Path.Combine(LogDir, "launcher.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        // Capturar excepciones no manejadas y escribirlas a log + mostrar al usuario
        AppDomain.CurrentDomain.UnhandledException += (_, ev) =>
        {
            LogException("UnhandledException", ev.ExceptionObject as Exception);
        };
        DispatcherUnhandledException += (_, ev) =>
        {
            LogException("DispatcherUnhandledException", ev.Exception);
            MessageBox.Show($"Error: {ev.Exception.Message}\n\nLog: {LogFile}",
                "Britannia Reborn — Error", MessageBoxButton.OK, MessageBoxImage.Error);
            ev.Handled = true;
            Shutdown(1);
        };

        // Manual shutdown: necesario para que ShowDialog() de PatcherWindow no
        // cierre la app entera al cerrarse antes de mostrar MainWindow.
        // Cambia a OnLastWindowClose tras mostrar MainWindow.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        base.OnStartup(e);

        try
        {
            Log("Launcher arrancado");

            // Comprobación de actualización del launcher.
            // Hacemos un Task.Run + Wait con timeout pequeño (5s) para evitar
            // bloquear indefinidamente el dispatcher si GitHub está caído. Si
            // tarda más, seguimos con el arranque y omitimos el check.
            UpdateInfo? updateInfo = null;
            try
            {
                var task = System.Threading.Tasks.Task.Run(
                    async () => await LauncherUpdater.CheckForUpdatesAsync()
                );
                if (task.Wait(TimeSpan.FromSeconds(5)))
                {
                    updateInfo = task.Result;
                }
                else
                {
                    Log("UpdateCheck: timeout 5s — saltando");
                }
            }
            catch (Exception updEx)
            {
                LogException("UpdateCheck", updEx);
            }

            if (updateInfo != null)
            {
                Log($"Update disponible: v{updateInfo.RemoteVersion}");
                var updateWin = new UpdateWindow(updateInfo);
                updateWin.ShowDialog();
                if (updateWin.ActualizacionEnCurso)
                {
                    return;
                }
            }

            // Verificar UO al arranque. Si no existe, mostrar wizard antes del login.
            var uoPath = LauncherCore.DetectarUoPath();
            var uoConfigPath = LauncherSettings.LoadUoPath();
            if (!string.IsNullOrEmpty(uoConfigPath) && LauncherCore.EsUoPathValido(uoConfigPath))
            {
                uoPath = uoConfigPath;
            }

            Log($"UO path detectado: {uoPath ?? "(ninguno)"}");

            if (string.IsNullOrEmpty(uoPath))
            {
                var wiz = new WizardWindow();
                if (wiz.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }
                uoPath = wiz.UoPathSeleccionado;
            }

            // Patcher: copia inicial UO oficial -> uodata/ + descarga .mul actualizados del shard.
            var patcher = new PatcherWindow(uoPath!);
            patcher.ShowDialog();
            if (!patcher.Exitoso)
            {
                Shutdown();
                return;
            }

            // ClassicUO usa uodata/ como -uopath (tiene los .mul vanilla + parches del shard)
            var main = new MainWindow(PatcherCore.GetDataPath());
            MainWindow = main;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            main.Show();
        }
        catch (Exception ex)
        {
            LogException("OnStartup", ex);
            MessageBox.Show($"Error al arrancar: {ex.Message}\n\nLog: {LogFile}",
                "Britannia Reborn — Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    public static void Log(string msg)
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            File.AppendAllText(LogFile, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] {msg}\n");
        }
        catch { }
    }

    public static void LogException(string ctx, Exception? ex)
    {
        if (ex == null) return;
        Log($"=== {ctx} ===\n{ex}\n=== fin ===");
    }
}

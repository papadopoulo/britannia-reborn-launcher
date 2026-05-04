using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace BritanniaReborn;

public partial class PatcherWindow : Window
{
    private readonly string _uoOficialPath;
    private readonly CancellationTokenSource _cts = new();
    // En modo post-game, la ventana reaparece tras cerrar ClassicUO; no hay que
    // descargar nada, solo mostrar el banner con el botón JUGAR para relogear.
    private readonly bool _postGame;

    public bool Exitoso { get; private set; }

    public PatcherWindow(string uoOficialPath, bool postGame = false)
    {
        InitializeComponent();
        _uoOficialPath = uoOficialPath;
        _postGame = postGame;
        MouseLeftButtonDown += (_, _) => { try { DragMove(); } catch { } };

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        LblLauncherVersion.Text = $"Britannia Reborn Launcher v{version}";

        if (_postGame)
        {
            // Sin descarga: directamente vista listo con BtnJugar para volver al login
            Loaded += (_, _) => MostrarVistaListo();
        }
        else
        {
            Loaded += async (_, _) => await EjecutarAsync();
        }
    }

    private async Task EjecutarAsync()
    {
        var prog = new Progress<PatcherProgress>(p =>
        {
            LblEstado.Text = p.Mensaje ?? "";
            LblArchivo.Text = p.TotalArchivos > 0 ? $"Archivo {p.ArchivoActual}/{p.TotalArchivos}" : "";
            ProgresoBar.Value = p.PorcentajeArchivo;
            LblPorcentaje.Text = $"{p.PorcentajeArchivo}%";
        });

        try
        {
            // PASO 1: si nunca hicimos copia inicial del UO oficial, hacerla.
            if (!PatcherCore.DataInicializado())
            {
                LblEstado.Text = "Primer arranque: copiando datos de UO oficial...";
                await PatcherCore.CopiarUoOficialInicialAsync(_uoOficialPath, prog, _cts.Token);
            }

            // PASO 2: descargar manifest y parchear archivos cambiables.
            await PatcherCore.ParchearAsync(prog, _cts.Token);

            // Cambiar a vista listo (login + enlaces + botón Jugar)
            MostrarVistaListo();
        }
        catch (OperationCanceledException)
        {
            Exitoso = false;
            DialogResult = false;
            Close();
        }
        catch (Exception ex)
        {
            App.LogException("Patcher", ex);
            LblError.Text = $"Error: {ex.Message}";
            BtnContinuarOffline.Visibility = Visibility.Visible;
        }
    }

    private void MostrarVistaListo()
    {
        // Ocultar panel descarga + el border que lo contiene + mostrar botón Jugar
        PanelDescarga.Visibility = Visibility.Collapsed;
        // El padre del PanelDescarga es el Border. Subir y ocultarlo.
        if (PanelDescarga.Parent is FrameworkElement parent) parent.Visibility = Visibility.Collapsed;
        LblError.Visibility = Visibility.Collapsed;
        BtnContinuarOffline.Visibility = Visibility.Collapsed;
        BtnJugar.Visibility = Visibility.Visible;
    }

    private void BtnJugar_Click(object sender, RoutedEventArgs e)
    {
        if (_postGame)
        {
            // Reabrimos el login. Cuidado con ShutdownMode: cerrar esta ventana
            // (que es Application.MainWindow en este punto) bajo OnMainWindowClose
            // tumbaría la app. Cambiamos a OnExplicitShutdown durante la transición.
            Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var login = new MainWindow(_uoOficialPath);
            Application.Current.MainWindow = login;
            login.Show();
            Application.Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
            Close();
            return;
        }

        // Modo normal (primera vez): App.OnStartup abre MainWindow tras esto
        Exitoso = true;
        DialogResult = true;
        Close();
    }

    private void BtnQuit_Click(object sender, RoutedEventArgs e)
    {
        Exitoso = false;
        // DialogResult solo se puede asignar si la ventana se abrió con
        // ShowDialog() — modo patcher inicial. En modo post-game se abrió
        // con Show() y asignar DialogResult lanza InvalidOperationException.
        if (!_postGame)
        {
            try { DialogResult = false; } catch { }
        }
        Close();
        Application.Current.Shutdown();
    }

    private void BtnContinuarOffline_Click(object sender, RoutedEventArgs e)
    {
        // Continua aunque no haya actualizado. Si los .mul locales sirven, pasa a la vista listo.
        if (PatcherCore.DataInicializado())
        {
            MostrarVistaListo();
        }
        else
        {
            LblError.Text = "No se puede continuar: el primer setup de archivos UO falló.";
        }
    }

    private void LinkWeb_Click(object sender, RoutedEventArgs e) => AbrirEnlace(sender);
    private void LinkDiscord_Click(object sender, RoutedEventArgs e) => AbrirEnlace(sender);
    private void LinkWiki_Click(object sender, RoutedEventArgs e) => AbrirEnlace(sender);
    private void LinkBugs_Click(object sender, RoutedEventArgs e) => AbrirEnlace(sender);

    private static void AbrirEnlace(object sender)
    {
        if (sender is Button b && b.Tag is string url && !string.IsNullOrEmpty(url))
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch { }
        }
    }
}

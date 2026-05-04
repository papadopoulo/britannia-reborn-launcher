using System;
using System.Threading;
using System.Windows;

namespace BritanniaReborn;

public partial class UpdateWindow : Window
{
    private readonly UpdateInfo _info;
    private readonly CancellationTokenSource _cts = new();

    public bool ActualizacionEnCurso { get; private set; }

    public UpdateWindow(UpdateInfo info)
    {
        InitializeComponent();
        _info = info;

        MouseLeftButtonDown += (_, _) => { try { DragMove(); } catch { } };

        var localVer = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "actual";
        LblTitulo.Text = $"v{_info.RemoteVersion.ToString(3)} disponible";
        LblMensaje.Text = $"Tienes la versión {localVer} y hay una versión más reciente con mejoras y correcciones.\n\n" +
                          "La actualización se descarga, se aplica automáticamente y el launcher se reinicia. Tarda 1-2 minutos.";
    }

    private async void BtnActualizar_Click(object sender, RoutedEventArgs e)
    {
        ActualizacionEnCurso = true;
        BtnActualizar.IsEnabled = false;
        BtnDespues.IsEnabled = false;
        BtnActualizar.Content = "DESCARGANDO...";
        PanelProgreso.Visibility = Visibility.Visible;
        LblError.Text = "";

        var progress = new Progress<DownloadProgress>(p =>
        {
            BarraProgreso.Value = p.Percent;
            LblProgreso.Text = p.BytesTotal > 0
                ? $"{p.Percent}% ({p.BytesDownloaded / 1024 / 1024} / {p.BytesTotal / 1024 / 1024} MB)"
                : $"{p.BytesDownloaded / 1024 / 1024} MB";
        });

        try
        {
            var zipPath = await LauncherUpdater.DownloadUpdateAsync(_info.DownloadUrl, progress, _cts.Token);

            BtnActualizar.Content = "APLICANDO...";
            LblMensaje.Text = "Descarga completada. Aplicando actualización y reiniciando el launcher...";

            // Pequeño delay para que el usuario vea el mensaje
            await System.Threading.Tasks.Task.Delay(800);

            LauncherUpdater.ApplyUpdateAndRestart(zipPath);
            // Application.Shutdown() ya se llamó dentro de ApplyUpdate.
        }
        catch (Exception ex)
        {
            App.LogException("UpdateWindow.Apply", ex);
            LblError.Text = $"Error al actualizar: {ex.Message}";
            BtnActualizar.IsEnabled = true;
            BtnDespues.IsEnabled = true;
            BtnActualizar.Content = "REINTENTAR";
            ActualizacionEnCurso = false;
        }
    }

    private void BtnDespues_Click(object sender, RoutedEventArgs e)
    {
        try { _cts.Cancel(); } catch { }
        Close();
    }
}

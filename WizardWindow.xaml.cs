using System.Windows;
using Microsoft.Win32;

namespace BritanniaReborn;

public partial class WizardWindow : Window
{
    public string? UoPathSeleccionado { get; private set; }

    public WizardWindow()
    {
        InitializeComponent();
        // Permitir mover ventana arrastrando
        MouseLeftButtonDown += (_, _) => { try { DragMove(); } catch { } };
    }

    private void BtnDescargar_Click(object sender, RoutedEventArgs e)
    {
        LauncherCore.AbrirDescargaUoOficial();
        LblEstado.Text = "Se ha abierto la web oficial. Tras instalarlo, vuelve a ejecutar el launcher.";
    }

    private void BtnYaTengo_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Selecciona la carpeta donde tienes instalado Ultima Online"
        };
        if (dlg.ShowDialog() != true) return;

        var path = dlg.FolderName;
        if (!LauncherCore.EsUoPathValido(path))
        {
            MessageBox.Show(
                "No se han encontrado archivos válidos de Ultima Online en esa carpeta. Asegúrate de seleccionar la carpeta raíz que contiene anim.mul y los .mul de mapas.",
                "Carpeta inválida",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        UoPathSeleccionado = path;
        LauncherSettings.SaveUoPath(path);
        DialogResult = true;
        Close();
    }

    private void BtnSalir_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

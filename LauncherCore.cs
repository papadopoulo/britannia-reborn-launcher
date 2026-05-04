using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace BritanniaReborn;

internal static class LauncherCore
{
    /// <summary>Detecta si UO oficial está instalado en alguna ruta conocida.</summary>
    public static string? DetectarUoPath()
    {
        foreach (var c in Config.CandidatosUoPath)
        {
            if (EsUoPathValido(c)) return c;
        }
        return null;
    }

    /// <summary>Verifica que una carpeta tenga los archivos esenciales de UO.</summary>
    public static bool EsUoPathValido(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return false;
        // anim.mul es el archivo de animaciones, imprescindible para mostrar mobs
        if (!File.Exists(Path.Combine(path, "anim.mul"))) return false;
        // map0.mul el mapa Felucca
        if (!File.Exists(Path.Combine(path, "map0.mul")) && !File.Exists(Path.Combine(path, "map0LegacyMUL.uop"))) return false;
        return true;
    }

    /// <summary>Localiza el ClassicUO embebido al lado del launcher.</summary>
    public static string GetCuoExePath()
    {
        var basePath = AppContext.BaseDirectory;
        return Path.Combine(basePath, Config.ClassicUoSubfolder, Config.ClassicUoExeName);
    }

    public static bool ExisteClassicUo() => File.Exists(GetCuoExePath());

    /// <summary>
    /// Lanza ClassicUO. Imita exactamente lo que hace play-digitalnest.bat
    /// (orden args, sin -clientversion, sin redirect que cuelgue al hijo).
    /// El proceso queda detached del launcher (al cerrar launcher cuo sigue).
    /// </summary>
    public static (bool Ok, string ErrorOut, Process? Proc) LanzarJuego(string username, string password, string uoPath)
    {
        var cuoExe = GetCuoExePath();
        if (!File.Exists(cuoExe))
        {
            return (false, $"cuo.exe no existe en {cuoExe}", null);
        }

        // Bug observado: ClassicUO con -skiploginscreen intenta auto-loguear con
        // el personaje de la cuenta anterior (lastcharacter.json) aunque pasemos
        // -username distinto. Resultado: cliente colgado en "Verifying account".
        //
        // Solución: si el username cambió, borrar lastcharacter.json para que
        // CUO no intente entrar a un personaje que no existe en la cuenta nueva.
        try
        {
            LimpiarCacheClienteSiCambioCuenta(cuoExe, username);
        }
        catch (Exception ex)
        {
            BritanniaReborn.App.Log($"No pude limpiar caché del cliente: {ex.Message}");
        }

        // -skiploginscreen SIEMPRE: las credenciales ya las metió el player
        // en NUESTRA pantalla login WPF, así que ClassicUO no debe mostrar la
        // suya. El arg -fastlogin que tenía antes NO existe en ClassicUO 1.1
        // (es ignorado silenciosamente). El correcto es -skiploginscreen
        // según ClassicUO/Main.cs:397.
        var args = new System.Collections.Generic.List<string>();
        args.Add("-skiploginscreen");
        if (!string.IsNullOrEmpty(username))
        {
            args.Add("-username");
            args.Add(username);
        }
        if (!string.IsNullOrEmpty(password))
        {
            args.Add("-password");
            args.Add(password);
        }
        args.Add("-ip"); args.Add(Config.ServerHost);
        args.Add("-port"); args.Add(Config.ServerPort.ToString());
        args.Add("-shardtype"); args.Add(Config.ShardType.ToString());
        args.Add("-uopath"); args.Add(uoPath);
        args.Add("-clientversion"); args.Add(Config.ClientVersion);

        try
        {
            BritanniaReborn.App.Log($"Lanzando: {cuoExe} {string.Join(" ", args)}");

            // UseShellExecute=false + CreateNoWindow=true:
            // - No se ve la consola de cuo (es console app)
            // - La ventana del juego SI se abre normal (no se ve afectada)
            // - Sin redirect: el proceso es independiente, sigue corriendo
            //   aunque cerremos el launcher
            var psi = new ProcessStartInfo
            {
                FileName = cuoExe,
                WorkingDirectory = Path.GetDirectoryName(cuoExe)!,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            var proc = Process.Start(psi);
            return (true, "", proc);
        }
        catch (Exception ex)
        {
            return (false, $"Excepción al lanzar: {ex.Message}", null);
        }
    }

    /// <summary>
    /// Si el username actual es distinto del último que jugó, borra los archivos
    /// que ClassicUO usa para recordar el último personaje (lastcharacter.json,
    /// globalprofile.json) y limpia las credenciales del settings.json. Sin
    /// esto, CUO con -skiploginscreen intenta auto-conectar al personaje de la
    /// cuenta anterior y queda colgado en "Verifying account".
    /// </summary>
    private static void LimpiarCacheClienteSiCambioCuenta(string cuoExePath, string newUsername)
    {
        var saved = LauncherSettings.Load();
        var oldUsername = saved.LastUsername;

        if (string.Equals(oldUsername, newUsername, StringComparison.OrdinalIgnoreCase))
        {
            return; // misma cuenta, conserva el último personaje
        }

        var cuoDir = Path.GetDirectoryName(cuoExePath)!;

        // Archivos de "último personaje" que CUO consulta para auto-loguear
        var rutasABorrar = new[]
        {
            Path.Combine(cuoDir, "Data", "Profiles", "lastcharacter.json"),
            Path.Combine(cuoDir, "Data", "Profiles", "globalprofile.json"),
        };
        foreach (var ruta in rutasABorrar)
        {
            try
            {
                if (File.Exists(ruta))
                {
                    File.Delete(ruta);
                    BritanniaReborn.App.Log($"Borrado: {ruta} (cambio de cuenta {oldUsername} → {newUsername})");
                }
            }
            catch (Exception ex)
            {
                BritanniaReborn.App.Log($"No pude borrar {ruta}: {ex.Message}");
            }
        }

        // Limpiar credenciales y last character del settings.json (si existe).
        // CUO lo recrea con los args -username -password al arrancar.
        var settingsFile = Path.Combine(cuoDir, "settings.json");
        try
        {
            if (File.Exists(settingsFile))
            {
                File.Delete(settingsFile);
                BritanniaReborn.App.Log($"Borrado: {settingsFile} (cambio de cuenta)");
            }
        }
        catch (Exception ex)
        {
            BritanniaReborn.App.Log($"No pude borrar settings.json: {ex.Message}");
        }
    }

    /// <summary>Abre la URL de descarga de UO oficial en el navegador del usuario.</summary>
    public static void AbrirDescargaUoOficial()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Config.UoOfficialDownloadUrl,
                UseShellExecute = true
            });
        }
        catch
        {
            // ignore
        }
    }
}

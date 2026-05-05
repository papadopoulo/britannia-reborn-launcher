using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

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

        // Reparar settings.json si tiene last_server_name vacío. Bug observado:
        // CUO regenerado deja last_server_name="" y queda colgado en "Logging
        // into shard" al conectar al game server.
        try
        {
            RepararSettingsCliente(cuoExe);
        }
        catch (Exception ex)
        {
            BritanniaReborn.App.Log($"No pude reparar settings.json del cliente: {ex.Message}");
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
    /// Si el username actual es distinto del último que jugó, borra el
    /// lastcharacter.json para que CUO con -skiploginscreen no intente
    /// auto-conectar al personaje de la cuenta anterior.
    ///
    /// IMPORTANTE: NO tocar settings.json. CUO lo necesita íntegro
    /// (lastservernum, last_server_name, configs cliente, etc.). Si lo
    /// borramos, CUO arranca con valores default incompletos y queda
    /// colgado intentando conectar al server. Las credenciales no se
    /// guardan en settings.json (saveaccount=false por defecto), se pasan
    /// por args -username -password en cada lanzamiento, así que no hay
    /// problema de credenciales viejas.
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
        var lastCharFile = Path.Combine(cuoDir, "Data", "Profiles", "lastcharacter.json");

        try
        {
            if (File.Exists(lastCharFile))
            {
                File.Delete(lastCharFile);
                BritanniaReborn.App.Log($"Borrado: {lastCharFile} (cambio de cuenta {oldUsername} → {newUsername})");
            }
        }
        catch (Exception ex)
        {
            BritanniaReborn.App.Log($"No pude borrar {lastCharFile}: {ex.Message}");
        }
    }

    /// <summary>
    /// Asegura que settings.json del cliente tiene last_server_name y lastservernum
    /// correctos. CUO al regenerar el archivo a veces los deja vacíos y entonces
    /// no sabe a qué shard conectar (queda colgado en "Logging into shard").
    /// </summary>
    private static void RepararSettingsCliente(string cuoExePath)
    {
        var settingsFile = Path.Combine(Path.GetDirectoryName(cuoExePath)!, "settings.json");
        if (!File.Exists(settingsFile))
        {
            return; // CUO lo creará al arrancar; cuando volvamos a lanzar lo reparamos
        }

        var json = File.ReadAllText(settingsFile);
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch
        {
            return; // JSON corrupto, no tocar
        }
        if (node is not JsonObject obj)
        {
            return;
        }

        var changed = false;

        var serverNameNode = obj["last_server_name"];
        var serverName = serverNameNode?.GetValue<string>() ?? "";
        if (string.IsNullOrEmpty(serverName))
        {
            obj["last_server_name"] = Config.ServerName;
            changed = true;
        }

        var serverNumNode = obj["lastservernum"];
        var serverNum = serverNumNode?.GetValue<int>() ?? 0;
        if (serverNum <= 0)
        {
            obj["lastservernum"] = Config.LastServerNum;
            changed = true;
        }

        if (changed)
        {
            File.WriteAllText(settingsFile, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            BritanniaReborn.App.Log($"Reparado settings.json: last_server_name={Config.ServerName}, lastservernum={Config.LastServerNum}");
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BritanniaReborn;

// Núcleo del patcher. Maneja:
// - Primer arranque: copia el UO oficial completo a uodata/ (one-time)
// - Descarga manifest.json del servidor con SHA256 esperado de cada archivo
// - Compara con archivos locales en uodata/, descarga los que difieren
// - ClassicUO se lanzará después con -uopath apuntando a uodata/

internal sealed class ManifestEntry
{
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
}

internal sealed class Manifest
{
    public string Version { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
    public List<ManifestEntry> Files { get; set; } = new();
}

public sealed class PatcherProgress
{
    public string Mensaje { get; set; } = "";
    public int ArchivoActual { get; set; }
    public int TotalArchivos { get; set; }
    public long BytesDescargados { get; set; }
    public long BytesTotales { get; set; }
    public int PorcentajeArchivo => BytesTotales > 0 ? (int)(BytesDescargados * 100 / BytesTotales) : 0;
}

internal static class PatcherCore
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };

    public static string GetDataPath() =>
        Path.Combine(AppContext.BaseDirectory, Config.DataSubfolder);

    /// <summary>True si el primer setup (copia desde UO oficial) ya está hecho.
    /// Verifica anim.mul + map0 + client.exe (necesario para ClassicUO detectar versión).</summary>
    public static bool DataInicializado()
    {
        var dataDir = GetDataPath();
        if (!Directory.Exists(dataDir)) return false;
        // Heurística: tener anim.mul + map0.mul + client.exe significa que ya copiamos.
        // client.exe es CRÍTICO: ClassicUO lee la versión de él si no se le pasa -clientversion.
        return File.Exists(Path.Combine(dataDir, "anim.mul"))
            && (File.Exists(Path.Combine(dataDir, "map0.mul"))
                || File.Exists(Path.Combine(dataDir, "map0LegacyMUL.uop")))
            && File.Exists(Path.Combine(dataDir, "client.exe"));
    }

    /// <summary>Copia todos los .mul/.uop/.idx/.def + client.exe del UO oficial a uodata/.</summary>
    public static async Task CopiarUoOficialInicialAsync(string uoOficialPath, IProgress<PatcherProgress> progress, CancellationToken ct)
    {
        var dst = GetDataPath();
        Directory.CreateDirectory(dst);

        // .exe incluido: client.exe lo necesita ClassicUO para detectar versión vanilla.
        var extensiones = new[] { ".mul", ".uop", ".idx", ".def", ".bin", ".enu", ".dat", ".tdf", ".exe", ".dll" };
        var archivos = new List<string>();
        foreach (var ext in extensiones)
        {
            archivos.AddRange(Directory.GetFiles(uoOficialPath, "*" + ext, SearchOption.TopDirectoryOnly));
        }

        var totalBytes = 0L;
        foreach (var f in archivos) totalBytes += new FileInfo(f).Length;

        var done = 0;
        var bytesDone = 0L;
        var prog = new PatcherProgress { TotalArchivos = archivos.Count, BytesTotales = totalBytes };

        foreach (var src in archivos)
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(src);
            var dstFile = Path.Combine(dst, name);
            prog.Mensaje = $"Copiando UO oficial: {name}";
            prog.ArchivoActual = ++done;
            prog.BytesDescargados = bytesDone;
            progress.Report(prog);

            await using var ssrc = File.OpenRead(src);
            await using var sdst = File.Create(dstFile);
            await ssrc.CopyToAsync(sdst, ct);
            bytesDone += new FileInfo(src).Length;
        }
        prog.BytesDescargados = totalBytes;
        prog.Mensaje = "UO oficial copiado a uodata/";
        progress.Report(prog);
    }

    /// <summary>Descarga manifest, compara con local, descarga los que difieren.</summary>
    public static async Task<int> ParchearAsync(IProgress<PatcherProgress> progress, CancellationToken ct)
    {
        var dataDir = GetDataPath();
        Directory.CreateDirectory(dataDir);

        progress.Report(new PatcherProgress { Mensaje = "Descargando manifest del servidor..." });
        Manifest? manifest;
        try
        {
            var json = await _http.GetStringAsync(Config.ManifestUrl, ct);
            manifest = JsonSerializer.Deserialize<Manifest>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            // Si no hay manifest (server caído, sin conexión), continuamos con lo que tengamos local.
            progress.Report(new PatcherProgress { Mensaje = $"Sin manifest disponible: {ex.Message}" });
            return 0;
        }
        if (manifest == null || manifest.Files.Count == 0)
        {
            progress.Report(new PatcherProgress { Mensaje = "Manifest vacío." });
            return 0;
        }

        var aActualizar = new List<ManifestEntry>();
        foreach (var entry in manifest.Files)
        {
            var local = Path.Combine(dataDir, entry.Name);
            if (!File.Exists(local) || ComputarSha256(local) != entry.Sha256)
            {
                aActualizar.Add(entry);
            }
        }

        if (aActualizar.Count == 0)
        {
            progress.Report(new PatcherProgress { Mensaje = "Mapa actualizado. Sin cambios." });
            return 0;
        }

        var done = 0;
        foreach (var entry in aActualizar)
        {
            ct.ThrowIfCancellationRequested();
            done++;
            var prog = new PatcherProgress
            {
                Mensaje = $"Descargando {entry.Name}...",
                ArchivoActual = done,
                TotalArchivos = aActualizar.Count,
                BytesTotales = entry.Size
            };
            progress.Report(prog);

            var url = Config.ArchivosBaseUrl + entry.Name;
            var local = Path.Combine(dataDir, entry.Name);
            var tmp = local + ".tmp";

            using (var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                await using var input = await resp.Content.ReadAsStreamAsync(ct);
                await using var output = File.Create(tmp);
                var buf = new byte[64 * 1024];
                int read;
                while ((read = await input.ReadAsync(buf, ct)) > 0)
                {
                    await output.WriteAsync(buf.AsMemory(0, read), ct);
                    prog.BytesDescargados += read;
                    progress.Report(prog);
                }
            }

            // Verifica hash antes de mover (evita corrupción en mitad de descarga)
            if (ComputarSha256(tmp) != entry.Sha256)
            {
                File.Delete(tmp);
                throw new InvalidDataException($"Hash inválido para {entry.Name} tras descarga.");
            }
            if (File.Exists(local)) File.Delete(local);
            File.Move(tmp, local);
        }

        progress.Report(new PatcherProgress
        {
            Mensaje = $"Actualización completada ({aActualizar.Count} archivos).",
            ArchivoActual = aActualizar.Count,
            TotalArchivos = aActualizar.Count
        });
        return aActualizar.Count;
    }

    private static string ComputarSha256(string ruta)
    {
        using var stream = File.OpenRead(ruta);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

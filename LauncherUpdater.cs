using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BritanniaReborn;

// Auto-update del launcher.
//
// Flujo:
//  1. CheckForUpdatesAsync() — llama a la GitHub Releases API y compara la
//     versión del último release con la del binario actual.
//  2. Si hay nueva versión, App.OnStartup muestra UpdateWindow.
//  3. Si el player acepta, DownloadAndApply() baja el zip, lo extrae a una
//     carpeta temporal, escribe un .bat updater que espera al cierre del
//     launcher actual, copia los archivos nuevos encima de los actuales,
//     y relanza el launcher. Después Application.Shutdown().
//
// El .bat es necesario porque Windows no permite sobrescribir un .exe en
// ejecución. El .bat espera 2-3s, comprueba que el proceso ya no existe
// y entonces hace el copy + start.
public sealed class UpdateInfo
{
    public required Version RemoteVersion { get; init; }
    public required string DownloadUrl { get; init; }
    public string ReleaseNotesUrl { get; init; } = "";
    public string ReleaseName { get; init; } = "";
}

public static class LauncherUpdater
{
    private const string GitHubApi =
        "https://api.github.com/repos/papadopoulo/britannia-reborn-launcher/releases/latest";

    public static async Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("BritanniaReborn-Launcher");
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            var json = await http.GetStringAsync(GitHubApi, ct);
            var release = JsonSerializer.Deserialize<GitHubRelease>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (release == null) return null;

            var remote = ParseVersion(release.TagName);
            var local = Assembly.GetExecutingAssembly().GetName().Version;
            if (remote == null || local == null) return null;
            if (remote.CompareTo(local) <= 0) return null;

            // Asset .zip del release (lo genera el GitHub Action)
            var asset = release.Assets?.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            if (asset == null) return null;

            return new UpdateInfo
            {
                RemoteVersion = remote,
                DownloadUrl = asset.BrowserDownloadUrl,
                ReleaseNotesUrl = release.HtmlUrl ?? "",
                ReleaseName = release.Name ?? release.TagName ?? "",
            };
        }
        catch (Exception ex)
        {
            App.LogException("LauncherUpdater.Check", ex);
            return null;
        }
    }

    private static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrEmpty(tag)) return null;
        var s = tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag[1..] : tag;
        // Strip pre-release/build metadata: "1.0.2-beta+abc" → "1.0.2"
        var dash = s.IndexOfAny(new[] { '-', '+' });
        if (dash > 0) s = s[..dash];
        return Version.TryParse(s, out var v) ? v : null;
    }

    /// <summary>Descarga el zip y muestra progreso. Devuelve el path del zip.</summary>
    public static async Task<string> DownloadUpdateAsync(
        string url,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "BritanniaRebornUpdate");
        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        Directory.CreateDirectory(tempDir);

        var zipPath = Path.Combine(tempDir, "update.zip");

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("BritanniaReborn-Launcher");

        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        var totalBytes = resp.Content.Headers.ContentLength ?? 0;
        await using var input = await resp.Content.ReadAsStreamAsync(ct);
        await using var output = File.Create(zipPath);

        var buf = new byte[64 * 1024];
        long read = 0;
        int n;
        while ((n = await input.ReadAsync(buf, ct)) > 0)
        {
            await output.WriteAsync(buf.AsMemory(0, n), ct);
            read += n;
            progress?.Report(new DownloadProgress { BytesDownloaded = read, BytesTotal = totalBytes });
        }
        return zipPath;
    }

    /// <summary>Extrae el zip y lanza el updater .bat. Cierra el launcher tras lanzar.</summary>
    public static void ApplyUpdateAndRestart(string zipPath)
    {
        var workDir = Path.GetDirectoryName(zipPath)!;
        var extractDir = Path.Combine(workDir, "extracted");
        if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
        ZipFile.ExtractToDirectory(zipPath, extractDir);

        // El zip tiene estructura BritanniaReborn/<exe + cuo/>. Buscamos esa
        // carpeta dentro del extract para copiar su contenido al currentDir.
        var sourceDir = Directory.GetDirectories(extractDir).FirstOrDefault() ?? extractDir;
        var currentDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var pid = System.Diagnostics.Process.GetCurrentProcess().Id;

        // Genera un .bat que: espera el cierre del proceso, copia, relanza
        var bat = Path.Combine(workDir, "br-updater.bat");
        File.WriteAllText(bat, BuildUpdaterScript(pid, sourceDir, currentDir));

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = bat,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workDir,
        };
        System.Diagnostics.Process.Start(psi);

        // Cerramos el launcher para liberar el .exe — el .bat espera
        // que se cierre antes de copiar.
        System.Windows.Application.Current.Shutdown();
    }

    private static string BuildUpdaterScript(int pid, string sourceDir, string currentDir)
    {
        // Espera 2s, comprueba que el PID ya no existe (loop), copia, relanza.
        // chcp 65001 = UTF-8 para mensajes (logs en consola si se quisiera ver).
        return $$"""
            @echo off
            chcp 65001 >nul
            timeout /t 2 /nobreak >nul
            :wait
            tasklist /FI "PID eq {{pid}}" 2>nul | find /I "{{pid}}" >nul
            if not errorlevel 1 (
                timeout /t 1 /nobreak >nul
                goto wait
            )
            xcopy "{{sourceDir}}\*" "{{currentDir}}\" /E /Y /Q >nul
            start "" "{{currentDir}}\BritanniaReborn.exe"
            exit
            """;
    }
}

public sealed class DownloadProgress
{
    public long BytesDownloaded { get; set; }
    public long BytesTotal { get; set; }
    public int Percent => BytesTotal > 0 ? (int)(BytesDownloaded * 100 / BytesTotal) : 0;
}

// DTOs de la GitHub API (subconjunto que necesitamos).
internal sealed class GitHubRelease
{
    public string? TagName { get; set; }
    public string? Name { get; set; }
    public string? HtmlUrl { get; set; }
    public GitHubAsset[]? Assets { get; set; }
}

internal sealed class GitHubAsset
{
    public string Name { get; set; } = "";
    public string BrowserDownloadUrl { get; set; } = "";
    public long Size { get; set; }
}

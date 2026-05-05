namespace BritanniaReborn;

// Configuración del shard. La IP se compila al binario; no aparece en
// texto plano si abres el .exe con notepad. Solo es visible si decompilas
// con dnSpy/ILSpy (estándar de shards UO).
internal static class Config
{
    public const string ServerHost = "134.255.219.238";
    public const int ServerPort = 2593;
    public const int ShardType = 2;

    // Nombre y número del shard tal como ModernUO los anuncia al cliente.
    // El launcher repara el settings.json de CUO con estos valores si están
    // vacíos (CUO regenerado a veces los pierde y queda colgado en
    // "Logging into shard").
    public const string ServerName = "DigitalNest";
    public const int LastServerNum = 1;

    // Versión de UO recomendada para que el cliente reporte
    public const string ClientVersion = "7.0.114.40";

    // URL oficial para que players sin UO se descarguen el cliente
    public const string UoOfficialDownloadUrl = "https://www.uo.com/client-download/";

    // Ubicaciones típicas donde se instala UO oficial
    public static readonly string[] CandidatosUoPath = new[]
    {
        @"C:\Program Files (x86)\Electronic Arts\Ultima Online Classic",
        @"C:\Program Files\Electronic Arts\Ultima Online Classic",
        @"C:\Program Files (x86)\Origin Games\Ultima Online Classic",
        @"C:\Program Files\Origin Games\Ultima Online Classic",
        @"C:\Program Files (x86)\EA Games\Ultima Online Classic",
        @"C:\Program Files\EA Games\Ultima Online Classic"
    };

    // Subcarpeta del launcher donde vive ClassicUO embebido
    public const string ClassicUoSubfolder = "cuo";
    public const string ClassicUoExeName = "cuo.exe";

    // Patcher: la VM del shard sirve los .mul actualizados via HTTP.
    // El launcher los descarga al primer arranque + cuando hay cambios.
    public const string PatcherBaseUrl = "http://134.255.219.238:8080";
    public const string ManifestUrl = PatcherBaseUrl + "/manifest.json";
    public const string ArchivosBaseUrl = PatcherBaseUrl + "/files/";
    public const string NewsUrl = PatcherBaseUrl + "/news.json";
    public const string EventosUrl = PatcherBaseUrl + "/eventos.json";

    // Polling para indicador "servidor online/offline" en pantalla login.
    // 3 segundos da feedback rápido tras restart sin saturar al server.
    public const int StatusCheckIntervalMs = 3000;
    public const int StatusCheckTimeoutMs = 1500;

    // Subcarpeta local donde el launcher mantiene los .mul parcheables.
    // ClassicUO usará esta como -uopath en lugar del UO oficial.
    // Al primer arranque se copia el contenido completo del UO oficial aquí
    // (operación one-time ~5GB). Después el patcher solo reemplaza los .mul
    // cambiables (map0.mul, staidx0.mul, statics0.mul).
    public const string DataSubfolder = "uodata";

    // Archivos que el patcher gestiona (cambiables desde el shard).
    // El mapa va en map0LegacyMUL.uop (formato cliente moderno), no map0.mul.
    public static readonly string[] ArchivosParcheable =
    {
        "map0LegacyMUL.uop",
        "staidx0.mul",
        "statics0.mul"
    };
}

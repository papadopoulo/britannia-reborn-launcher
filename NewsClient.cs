using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BritanniaReborn;

// Cliente para news.json — un JSON sirvido por el mismo nginx donde está el
// manifest. Permite actualizar las noticias del launcher sin recompilar:
// editas /srv/uo-mul/news.json en el VPS y los players ven los cambios al
// abrir el launcher.

public sealed class NoticiaItem
{
    public string Fecha { get; set; } = "";
    public string Titulo { get; set; } = "";
    public string Texto { get; set; } = "";
}

internal sealed class NewsPayload
{
    public List<NoticiaItem> Items { get; set; } = new();
}

internal static class NewsClient
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public static async Task<List<NoticiaItem>> CargarAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await _http.GetStringAsync(Config.NewsUrl, ct);
            var payload = JsonSerializer.Deserialize<NewsPayload>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return payload?.Items ?? new();
        }
        catch
        {
            return new();
        }
    }
}

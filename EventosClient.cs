using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BritanniaReborn;

// Cliente para eventos.json — JSON sirvido por el mismo nginx donde está
// manifest/news. Lista de eventos del día con hora, título y lugar.
// Se edita en /srv/uo-mul/eventos.json sin recompilar el launcher.

public sealed class EventoItem
{
    public string Hora { get; set; } = "";
    public string Titulo { get; set; } = "";
    public string Lugar { get; set; } = "";
}

internal sealed class EventosPayload
{
    public List<EventoItem> Items { get; set; } = new();
}

internal static class EventosClient
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public static async Task<List<EventoItem>> CargarAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await _http.GetStringAsync(Config.EventosUrl, ct);
            var payload = JsonSerializer.Deserialize<EventosPayload>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return payload?.Items ?? new();
        }
        catch
        {
            return new();
        }
    }
}

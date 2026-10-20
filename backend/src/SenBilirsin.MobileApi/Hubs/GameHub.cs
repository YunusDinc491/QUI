using Microsoft.AspNetCore.SignalR;

namespace SenBilirsin.MobileApi.Hubs;

/// <summary>
/// Oyun hub'ı. Faz 0'da yalnızca bağlantı prototipi için Echo metodu var;
/// eşleşme, soru akışı ve cevap olayları Faz 3-4'te eklenecek (bkz. docs/04-signalr-ve-api.md).
/// </summary>
public class GameHub : Hub
{
    /// <summary>İstemcinin gönderdiği metni geri döndürür (bağlantı testi).</summary>
    public Task<string> Echo(string message) => Task.FromResult($"echo: {message}");
}

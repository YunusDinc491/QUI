using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;

namespace SenBilirsin.Modules.MatchEngine.Application;

/// <summary>
/// Ödüllü reklam kapısı (mimari ilke 6): bir reklam niyeti yalnızca sunucu doğrulamasıyla (SSV) bir kez harcanır.
/// Gerçek uygulama Faz 7'de gelir (<c>ad_intents</c> + SSV); o zaman bu arayüzün yeni gerçeklemesi kaydedilir.
/// </summary>
public interface IRewardedAdGate
{
    /// <summary>Niyet geçerli ve daha önce kullanılmamışsa kullanıldı olarak işaretler ve true döner.</summary>
    Task<bool> TryConsumeAsync(Guid userId, string? intentId, string purpose, CancellationToken ct = default);
}

/// <summary>
/// Faz 3 için geçici kapı: yalnızca <c>Ads:TestMode=true</c> iken ve <c>test-</c> ile başlayan niyet kimlikleri kabul edilir
/// (her kimlik bir kez). Üretimde <c>Ads:TestMode</c> kapalıdır ve hiçbir devam/ödül verilmez — sahte ödül yolu yoktur.
/// </summary>
public sealed class TestRewardedAdGate(IConfiguration config) : IRewardedAdGate
{
    private readonly ConcurrentDictionary<string, byte> _used = new();

    public Task<bool> TryConsumeAsync(Guid userId, string? intentId, string purpose, CancellationToken ct = default)
    {
        if (!config.GetValue<bool>("Ads:TestMode")) return Task.FromResult(false);
        if (string.IsNullOrWhiteSpace(intentId) || !intentId.StartsWith("test-", StringComparison.Ordinal) || intentId.Length > 100)
            return Task.FromResult(false);
        return Task.FromResult(_used.TryAdd($"{userId:N}:{purpose}:{intentId}", 0));
    }
}

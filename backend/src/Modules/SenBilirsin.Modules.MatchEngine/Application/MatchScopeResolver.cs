using SenBilirsin.Modules.Catalog.Application;
using SenBilirsin.Modules.Catalog.Domain;

namespace SenBilirsin.Modules.MatchEngine.Application;

public sealed record MatchScope(string ModeCode, int? CategoryId, IReadOnlyCollection<int> CategoryIds, (string Code, string Message, int Status)? Error);

/// <summary>Seçilen mod/alt dalı (ya da "Karışık"ı) soru havuzu kategorilerine çevirir; yayında olmayanları reddeder.</summary>
public sealed class MatchScopeResolver(ICatalogQueries catalog)
{
    public async Task<MatchScope> ResolveAsync(string? modeCode, int? categoryId, CancellationToken ct)
    {
        string[] modes = [ModeCodes.General, ModeCodes.Branch, ModeCodes.Kpss, ModeCodes.Fan];
        if (modeCode is null || !modes.Contains(modeCode)) return new("", null, [], ("MODE_INVALID", "Geçersiz oyun modu.", 400));

        if (modeCode == ModeCodes.General)
        {
            var general = await catalog.GetCategoryAsync(1, ct);
            return general is { IsPublished: true }
                ? new(modeCode, general.Id, [general.Id], null)
                : new(modeCode, null, [], ("MODE_UNAVAILABLE", "Bu mod henüz kullanıma açılmadı.", 409));
        }

        if (categoryId is { } id)
        {
            var category = await catalog.GetCategoryAsync(id, ct);
            if (category is null || category.ModeCode != modeCode || !category.IsPublished)
                return new(modeCode, null, [], ("CATEGORY_UNAVAILABLE", "Bu kategori bulunamadı ya da kullanıma açık değil.", 404));
            return new(modeCode, id, [id], null);
        }

        // "Karışık": o moddaki yayındaki tüm alt dallar
        var published = await catalog.GetPublishedCategoryIdsAsync(modeCode, ct);
        return published.Count == 0
            ? new(modeCode, null, [], ("MODE_UNAVAILABLE", "Bu modda henüz yayında alt dal yok.", 409))
            : new(modeCode, null, published, null);
    }
}

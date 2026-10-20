namespace SenBilirsin.AdminApi.Configuration;

/// <summary>
/// Yalnızca geliştirmede: depo kökündeki <c>.env</c> dosyasını okuyup ortam değişkeni olarak ekler.
/// Zaten tanımlı olan değişkenlere dokunmaz. Üretimde kullanılmaz (değerler Railway ortam değişkenlerinden gelir).
/// MobileApi'deki aynı adlı sınıfın küçük bir kopyasıdır; iki host birbirine proje referansı vermesin diye ayrı tutuldu.
/// </summary>
public static class DevEnvFile
{
    public static void LoadIfDevelopment()
    {
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                  ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        if (!string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase)) return;

        var path = FindUpwards(".env");
        if (path is null) return;

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (value.Length == 0) continue; // boş değerler ayarları ezmesin
            if (Environment.GetEnvironmentVariable(key) is null)
                Environment.SetEnvironmentVariable(key, value);
        }
    }

    /// <summary>Geçerli klasörden ve uygulamanın bulunduğu klasörden başlayıp yukarı doğru dosyayı arar.</summary>
    public static string? FindUpwards(string fileName)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, fileName);
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
        }

        return null;
    }
}

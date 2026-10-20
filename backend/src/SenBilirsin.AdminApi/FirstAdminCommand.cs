using SenBilirsin.AdminApi.Configuration;
using SenBilirsin.Modules.Administration;
using SenBilirsin.Modules.Administration.Application;

namespace SenBilirsin.AdminApi;

/// <summary>
/// Yerel komut: <c>dotnet run --project backend/src/SenBilirsin.AdminApi -- create-admin ornek@mail.com</c>.
/// Rastgele parola + TOTP anahtarı üretir ve <b>yalnızca</b> depo kökündeki <c>.local/</c> klasöründeki (git'e girmeyen)
/// bir dosyaya yazar. Parola ve anahtar ekrana, loga ya da sohbete basılmaz (CLAUDE.md, mimari ilke 5).
/// </summary>
public static class FirstAdminCommand
{
    public const string Name = "create-admin";

    public static async Task<int> RunAsync(WebApplication app, string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Kullanım: create-admin <e-posta>");
            return 1;
        }

        await app.Services.MigrateAdministrationAsync();

        using var scope = app.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AdminAuthService>();
        CreatedAdmin created;
        try
        {
            created = await auth.CreateAdminAsync(args[1]);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            Console.Error.WriteLine("Yönetici oluşturulamadı: " + ex.Message);
            return 2;
        }

        var envPath = DevEnvFile.FindUpwards(".env");
        var root = envPath is null ? Directory.GetCurrentDirectory() : Path.GetDirectoryName(envPath)!;
        var folder = Path.Combine(root, ".local");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "admin-" + created.Email.Replace('@', '_') + ".txt");
        await File.WriteAllTextAsync(file, string.Join(Environment.NewLine,
            "QU¿ Yönetim — ilk giriş bilgileri (bu dosyayı kimseyle paylaşma; giriş yaptıktan sonra parolayı değiştir)",
            "E-posta: " + created.Email,
            "Parola:  " + created.Password,
            "TOTP gizli anahtarı (kimlik doğrulayıcı uygulamaya elle ekle): " + created.TotpSecret,
            "otpauth adresi: " + created.OtpAuthUri,
            ""));

        Console.WriteLine("Yönetici oluşturuldu: " + created.Email);
        Console.WriteLine("Giriş bilgileri şu dosyaya yazıldı (ekrana basılmadı): " + file);
        return 0;
    }
}

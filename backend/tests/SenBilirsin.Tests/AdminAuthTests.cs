using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SenBilirsin.Modules.Administration.Application;
using SenBilirsin.Modules.Administration.Data;
using SenBilirsin.Modules.Administration.Security;

namespace SenBilirsin.Tests;

/// <summary>Faz 2.3: yönetici girişi (parola + TOTP), kilitlenme, oturum belirteci, parola değişimi, denetim kaydı.</summary>
[Collection("admin-api")]
public class AdminAuthTests(AdminApiFactory factory)
{
    private const string Password = "Test-Parola-12345";

    private async Task<CreatedAdmin> NewAdminAsync(string? password = Password)
    {
        using var scope = factory.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AdminAuthService>();
        return await auth.CreateAdminAsync($"test-{Guid.NewGuid():N}@example.com", password);
    }

    private string CodeFor(CreatedAdmin admin, int stepOffset = 0) =>
        TotpService.ComputeCode(Base32.Decode(admin.TotpSecret), TotpService.StepFor(factory.Clock.GetUtcNow()) + stepOffset);

    private Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string? password, string? code) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password, code });

    private static async Task<string> TokenOf(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("token").GetString()!;
    }

    [Fact]
    public async Task Health_Works_WithAdminModule()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health");
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Login_WithCorrectPasswordAndCode_ReturnsToken_ThatOpensProtectedEndpoint()
    {
        var admin = await NewAdminAsync();
        using var client = factory.CreateClient();

        var response = await LoginAsync(client, admin.Email, Password, CodeFor(admin));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await TokenOf(response));
        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var body = await me.Content.ReadAsStringAsync();
        Assert.Contains(admin.Email, body);
        Assert.Contains("admin", body);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutOrWithBadToken_Returns401()
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "bu.gecersiz.token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Login_WrongPassword_WrongCode_UnknownEmail_AllGiveSameGenericError()
    {
        var admin = await NewAdminAsync();
        using var client = factory.CreateClient();

        var wrongPassword = await LoginAsync(client, admin.Email, "Yanlis-Parola-999", CodeFor(admin));
        var wrongCode = await LoginAsync(client, admin.Email, Password, "000000");
        var unknown = await LoginAsync(client, "yok@example.com", Password, "123456");

        foreach (var r in new[] { wrongPassword, wrongCode, unknown })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
            Assert.Contains(AdminAuthService.GenericFailure, await r.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task Login_EmailIsCaseInsensitive()
    {
        var admin = await NewAdminAsync();
        using var client = factory.CreateClient();
        var response = await LoginAsync(client, admin.Email.ToUpperInvariant(), Password, CodeFor(admin));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_SameTotpCodeCannotBeUsedTwice_ButNextStepCan()
    {
        var admin = await NewAdminAsync();
        using var client = factory.CreateClient();
        var code = CodeFor(admin);

        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, admin.Email, Password, code)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, admin.Email, Password, code)).StatusCode);

        // Bir sonraki 30 sn'lik adımın kodu (saat toleransı içinde) kabul edilir
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, admin.Email, Password, CodeFor(admin, 1))).StatusCode);
    }

    [Fact]
    public async Task Login_LocksAccountAfterFiveFailures_ThenUnlocksAfterTheLockPeriod()
    {
        var admin = await NewAdminAsync();
        using var client = factory.CreateClient();

        for (var i = 0; i < AdminAuthService.MaxFailedAttempts; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, admin.Email, "Yanlis-Parola-999", "000000")).StatusCode);

        // Kilitliyken doğru bilgi bile reddedilir
        var locked = await LoginAsync(client, admin.Email, Password, CodeFor(admin));
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);

        // Kilit süresi dolunca (servis düzeyinde) tekrar girilebilir
        factory.Clock.Advance(AdminAuthService.LockDuration + TimeSpan.FromSeconds(1));
        try
        {
            using var scope = factory.Services.CreateScope();
            var auth = scope.ServiceProvider.GetRequiredService<AdminAuthService>();
            var result = await auth.LoginAsync(admin.Email, Password, CodeFor(admin), "test");
            Assert.True(result.Success);
        }
        finally
        {
            // Diğer testler gerçek saatle imzalanan belirteçleri doğruladığı için saati geri getir
            factory.Clock.Advance(-(AdminAuthService.LockDuration + TimeSpan.FromSeconds(1)));
        }
    }

    [Fact]
    public async Task SuccessfulLogin_ResetsFailureCounter()
    {
        var admin = await NewAdminAsync();
        using var client = factory.CreateClient();

        for (var i = 0; i < AdminAuthService.MaxFailedAttempts - 1; i++)
            await LoginAsync(client, admin.Email, "Yanlis-Parola-999", "000000");
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, admin.Email, Password, CodeFor(admin))).StatusCode);

        // Sayaç sıfırlandığı için 4 yeni hata daha kilitlemez
        for (var i = 0; i < AdminAuthService.MaxFailedAttempts - 1; i++)
            await LoginAsync(client, admin.Email, "Yanlis-Parola-999", "000000");
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, admin.Email, Password, CodeFor(admin, 1))).StatusCode);
    }

    [Fact]
    public async Task ChangePassword_RequiresCurrentPassword_AndNewOneWorks()
    {
        var admin = await NewAdminAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await TokenOf(await LoginAsync(client, admin.Email, Password, CodeFor(admin))));

        var wrongCurrent = await client.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = "Yanlis-Parola-999", newPassword = "Yeni-Parola-67890" });
        Assert.Equal(HttpStatusCode.BadRequest, wrongCurrent.StatusCode);

        var tooShort = await client.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = Password, newPassword = "kisa" });
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);

        var ok = await client.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = Password, newPassword = "Yeni-Parola-67890" });
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);

        using var fresh = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(fresh, admin.Email, Password, CodeFor(admin, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(fresh, admin.Email, "Yeni-Parola-67890", CodeFor(admin, 1))).StatusCode);
    }

    [Fact]
    public async Task CreateAdmin_GeneratesStrongRandomPassword_StoresNoPlainSecrets_AndRejectsDuplicates()
    {
        var admin = await NewAdminAsync(password: null);
        Assert.True(admin.Password.Length >= 20);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AdministrationDbContext>();
        var row = await db.AdminUsers.SingleAsync(a => a.Email == admin.Email);
        Assert.DoesNotContain(admin.Password, row.PasswordHash);
        Assert.DoesNotContain(admin.TotpSecret, row.TotpSecretProtected);
        Assert.Equal(admin.TotpSecret, scope.ServiceProvider.GetRequiredService<SecretProtector>().Unprotect(row.TotpSecretProtected));

        var auth = scope.ServiceProvider.GetRequiredService<AdminAuthService>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => auth.CreateAdminAsync(admin.Email));
        await Assert.ThrowsAsync<ArgumentException>(() => auth.CreateAdminAsync("baska@example.com", "kisa"));
        await Assert.ThrowsAsync<ArgumentException>(() => auth.CreateAdminAsync("eposta-degil", Password));
    }

    [Fact]
    public async Task AuditLog_RecordsLoginSuccessAndFailure_WithoutSecrets()
    {
        var admin = await NewAdminAsync();
        using var client = factory.CreateClient();
        await LoginAsync(client, admin.Email, "Yanlis-Parola-999", "000000");
        await LoginAsync(client, admin.Email, Password, CodeFor(admin));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AdministrationDbContext>();
        var logs = await db.AuditLogs.Where(l => l.AdminId == admin.Id).Select(l => l.Action).ToListAsync();
        Assert.Contains("admin_created", logs);
        Assert.Contains("login_failed", logs);
        Assert.Contains("login", logs);

        var everything = string.Join(' ', await db.AuditLogs.Where(l => l.AdminId == admin.Id)
            .Select(l => l.Detail + l.Before + l.After).ToListAsync());
        Assert.DoesNotContain(Password, everything);
        Assert.DoesNotContain(admin.TotpSecret, everything);
    }
}

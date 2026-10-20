using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SenBilirsin.Tests;

/// <summary>Faz 1 kullanıcı uçlarının gerçek PostgreSQL üzerindeki entegrasyon testleri (DevAuth ile).</summary>
[Collection("api")]
public class UsersApiTests(MobileApiFactory factory)
{
    private static string NewUsername() => "u" + Guid.NewGuid().ToString("N")[..10];

    private HttpClient Client(string uid, string? deviceId = "device-1", bool verified = true, string provider = "google.com")
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Dev uid={uid};email={uid}@test.local;verified={verified.ToString().ToLowerInvariant()};provider={provider}");
        if (deviceId is not null) client.DefaultRequestHeaders.Add("X-Device-Id", deviceId);
        return client;
    }

    private static object ValidRegistration(string username, int birthYear = 2000, bool terms = true, string displayName = "Test Kullanıcı") =>
        new { username, displayName, avatarId = 1, birthYear, termsAccepted = terms };

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<(HttpClient client, string uid, string username)> RegisteredUser(string deviceId = "device-1")
    {
        var uid = Guid.NewGuid().ToString("N");
        var username = NewUsername();
        var client = Client(uid, deviceId);
        var register = await client.PostAsJsonAsync("/api/users/me", ValidRegistration(username));
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var session = await client.PostAsJsonAsync("/api/users/me/session", new { deviceId, platform = "test" });
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        return (client, uid, username);
    }

    [Fact]
    public async Task MobileApi_Health_IsHealthy()
    {
        var response = await factory.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Config_ReturnsVersionAndMaintenanceFlags()
    {
        var response = await factory.CreateClient().GetAsync("/api/config");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await Json(response);
        Assert.Equal("0.0.0", json.GetProperty("minSupportedVersion").GetString());
        Assert.False(json.GetProperty("maintenanceMode").GetBoolean());
    }

    [Fact]
    public async Task Me_WithoutAuthentication_Returns401()
    {
        var response = await factory.CreateClient().GetAsync("/api/users/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Register_Then_Session_Then_Me_Works()
    {
        var (client, _, username) = await RegisteredUser();

        var me = await client.GetAsync("/api/users/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var json = await Json(me);
        Assert.Equal(username, json.GetProperty("username").GetString());
        Assert.Equal("Test Kullanıcı", json.GetProperty("displayName").GetString());
        Assert.Equal("Active", json.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Register_Twice_ReturnsConflict()
    {
        var uid = Guid.NewGuid().ToString("N");
        var client = Client(uid);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/users/me", ValidRegistration(NewUsername()))).StatusCode);

        var second = await client.PostAsJsonAsync("/api/users/me", ValidRegistration(NewUsername()));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("ALREADY_REGISTERED", (await Json(second)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Register_SameUsernameFromAnotherUser_ReturnsUsernameTaken()
    {
        var (_, _, username) = await RegisteredUser();

        var other = Client(Guid.NewGuid().ToString("N"));
        var response = await other.PostAsJsonAsync("/api/users/me", ValidRegistration(username.ToUpperInvariant()));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("USERNAME_TAKEN", (await Json(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Register_PasswordProviderWithUnverifiedEmail_Returns403()
    {
        var client = Client(Guid.NewGuid().ToString("N"), verified: false, provider: "password");
        var response = await client.PostAsJsonAsync("/api/users/me", ValidRegistration(NewUsername()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("EMAIL_NOT_VERIFIED", (await Json(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Register_PasswordProviderWithVerifiedEmail_Succeeds()
    {
        var client = Client(Guid.NewGuid().ToString("N"), verified: true, provider: "password");
        var response = await client.PostAsJsonAsync("/api/users/me", ValidRegistration(NewUsername()));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Theory]
    [InlineData("TERMS", "TERMS_NOT_ACCEPTED")]
    [InlineData("AGE", "AGE_TOO_YOUNG")]
    [InlineData("PROFANE", "USERNAME_PROFANE")]
    [InlineData("SHORT", "USERNAME_TOO_SHORT")]
    public async Task Register_InvalidInput_ReturnsBadRequestWithCode(string scenario, string expectedCode)
    {
        var client = Client(Guid.NewGuid().ToString("N"));
        object body = scenario switch
        {
            "TERMS" => ValidRegistration(NewUsername(), terms: false),
            "AGE" => ValidRegistration(NewUsername(), birthYear: DateTime.UtcNow.Year - 10),
            "PROFANE" => ValidRegistration("orospu"),
            _ => ValidRegistration("ab"),
        };

        var response = await client.PostAsJsonAsync("/api/users/me", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedCode, (await Json(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task UsernameAvailable_ReportsTakenInvalidAndFree()
    {
        var (client, _, taken) = await RegisteredUser();

        var takenJson = await Json(await client.GetAsync($"/api/users/username-available?username={taken}"));
        Assert.False(takenJson.GetProperty("available").GetBoolean());
        Assert.Equal("USERNAME_TAKEN", takenJson.GetProperty("code").GetString());

        var invalidJson = await Json(await client.GetAsync("/api/users/username-available?username=a-b"));
        Assert.False(invalidJson.GetProperty("available").GetBoolean());

        var freeJson = await Json(await client.GetAsync($"/api/users/username-available?username={NewUsername()}"));
        Assert.True(freeJson.GetProperty("available").GetBoolean());
    }

    [Fact]
    public async Task Avatars_Returns_FiveFreeAvatars()
    {
        var client = Client(Guid.NewGuid().ToString("N"));
        var json = await Json(await client.GetAsync("/api/users/avatars"));
        Assert.Equal(5, json.GetArrayLength());
        Assert.All(json.EnumerateArray(), a => Assert.Equal(0, a.GetProperty("price").GetInt32()));
    }

    [Fact]
    public async Task SecondDevice_ReplacesFirst_Session()
    {
        var (device1, uid, _) = await RegisteredUser("device-1");

        // Aynı hesapla ikinci cihaz oturum açar
        var device2 = Client(uid, "device-2");
        Assert.Equal(HttpStatusCode.OK, (await device2.PostAsJsonAsync("/api/users/me/session", new { deviceId = "device-2", platform = "test" })).StatusCode);

        var oldDevice = await device1.GetAsync("/api/users/me");
        Assert.Equal(HttpStatusCode.Unauthorized, oldDevice.StatusCode);
        Assert.Equal("SESSION_REPLACED", (await Json(oldDevice)).GetProperty("code").GetString());

        Assert.Equal(HttpStatusCode.OK, (await device2.GetAsync("/api/users/me")).StatusCode);
    }

    [Fact]
    public async Task Me_WithoutDeviceHeader_ReturnsSessionReplaced()
    {
        var (_, uid, _) = await RegisteredUser();
        var noDevice = Client(uid, deviceId: null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await noDevice.GetAsync("/api/users/me")).StatusCode);
    }

    [Fact]
    public async Task Session_ForUnregisteredUser_Returns404()
    {
        var client = Client(Guid.NewGuid().ToString("N"));
        var response = await client.PostAsJsonAsync("/api/users/me/session", new { deviceId = "d", platform = "test" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("NOT_REGISTERED", (await Json(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task UpdateProfile_ChangesDisplayNameAndAvatar_ButRejectsProfaneAndUnknownAvatar()
    {
        var (client, _, _) = await RegisteredUser();

        var ok = await client.PatchAsJsonAsync("/api/users/me", new { displayName = "Yeni Ad", avatarId = 3 });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var json = await Json(ok);
        Assert.Equal("Yeni Ad", json.GetProperty("displayName").GetString());
        Assert.Equal(3, json.GetProperty("avatarId").GetInt32());

        var profane = await client.PatchAsJsonAsync("/api/users/me", new { displayName = "Orospu" });
        Assert.Equal(HttpStatusCode.BadRequest, profane.StatusCode);

        var unknownAvatar = await client.PatchAsJsonAsync("/api/users/me", new { avatarId = 999 });
        Assert.Equal(HttpStatusCode.BadRequest, unknownAvatar.StatusCode);
    }

    [Fact]
    public async Task DeleteAccount_AnonymizesUser_AndFreesUsername()
    {
        var (client, uid, username) = await RegisteredUser();

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/users/me")).StatusCode);

        // Hesap artık yok
        var after = await client.GetAsync("/api/users/me");
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);

        // Aynı Firebase kimliği için yeniden kayıt gerekir; eski kullanıcı adı yeniden alınabilir
        var reborn = await Client(uid, "device-9").PostAsJsonAsync("/api/users/me", ValidRegistration(username));
        Assert.Equal(HttpStatusCode.Created, reborn.StatusCode);
    }
}

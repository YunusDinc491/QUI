namespace SenBilirsin.Tests;

/// <summary>Faz 0: Admin API'nin ayağa kalktığını ve /health ucunun yanıt verdiğini doğrular.
/// (Faz 2.3'ten beri Admin API yapılandırma/anahtar gerektirdiği için AdminApiFactory ile, veritabanlı çalışır.)</summary>
[Collection("admin-api")]
public class HealthEndpointTests(AdminApiFactory factory)
{
    [Fact]
    public async Task AdminApi_Health_ReturnsHealthy()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}

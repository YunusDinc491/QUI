using Microsoft.AspNetCore.Mvc.Testing;

namespace SenBilirsin.Tests;

/// <summary>Faz 0: Admin API'nin ayağa kalktığını ve /health ucunun yanıt verdiğini doğrular.
/// (MobileApi'nin sağlık testi artık veritabanlı entegrasyon testleri içindedir: UsersApiTests.)</summary>
public class HealthEndpointTests
{
    [Fact]
    public async Task AdminApi_Health_ReturnsHealthy()
    {
        await using var factory = new WebApplicationFactory<AdminApiMarker>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}

using Microsoft.AspNetCore.Http;

namespace SenBilirsin.Modules.Users.Api;

/// <summary>
/// Diğer modüllerin uçlarında, <see cref="ActiveDeviceFilter"/>'dan geçmiş isteğin sahibi olan oyuncuyu verir.
/// (Filtre uygulanmamış bir uçta çağrılırsa hata fırlatır; böylece cihaz denetimi unutulamaz.)
/// </summary>
public static class ActivePlayer
{
    public static Guid IdOf(HttpContext http) => UsersEndpoints.CurrentUser(http).Id;

    public static string DisplayNameOf(HttpContext http) => UsersEndpoints.CurrentUser(http).DisplayName;
}

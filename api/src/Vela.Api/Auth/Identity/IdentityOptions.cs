namespace Vela.Api.Auth.Identity;

public sealed class IdentityOptions
{
    public const string Section = "Auth";

    public AppleOptions Apple { get; set; } = new();
    public GoogleOptions Google { get; set; } = new();

    public sealed class AppleOptions { public string ClientId { get; set; } = ""; }
    public sealed class GoogleOptions { public string[] ClientIds { get; set; } = []; }
}

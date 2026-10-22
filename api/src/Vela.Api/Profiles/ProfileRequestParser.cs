using System.Text.Json;

namespace Vela.Api.Profiles;

/// <summary>Reads request bodies by hand so absent, null and wrongly typed fields are told apart.</summary>
public static class ProfileRequestParser
{
    public static OnboardingRequest ParseOnboarding(JsonElement body)
    {
        var obj = RequireObject(body);
        var ids = new List<Guid>();
        if (obj.TryGetProperty("followArtistIds", out var arr) && arr.ValueKind != JsonValueKind.Null)
        {
            if (arr.ValueKind != JsonValueKind.Array) throw ProfileException.Validation("followArtistIds", "must be an array.");
            foreach (var e in arr.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.String || !Guid.TryParse(e.GetString(), out var id))
                    throw ProfileException.Validation("followArtistIds", "must contain ids.");
                ids.Add(id);
            }
        }
        return new OnboardingRequest(Str(obj, "displayName") ?? "", Str(obj, "handle") ?? "",
            Bool(obj, "ageConfirmed"), Bool(obj, "termsAccepted"), ids);
    }

    public static UpdateProfileRequest ParseUpdate(JsonElement body)
    {
        var obj = RequireObject(body);
        var clearEmail = obj.TryGetProperty("publicEmail", out var pe) && pe.ValueKind == JsonValueKind.Null;
        return new UpdateProfileRequest
        {
            DisplayName = Str(obj, "displayName"), Handle = Str(obj, "handle"), Bio = Str(obj, "bio"),
            City = Str(obj, "city"), Discipline = Str(obj, "discipline"), Availability = Str(obj, "availability"),
            Website = Str(obj, "website"), Social = Str(obj, "social"), Studio = Str(obj, "studio"),
            ReplyTime = Str(obj, "replyTime"), PublicEmail = Str(obj, "publicEmail"), ClearPublicEmail = clearEmail,
            Services = List(obj, "services", e => new ServiceDto(
                Str(e, "title", "services") ?? "", Str(e, "description", "services") ?? "", Str(e, "price", "services") ?? "")),
            Shows = List(obj, "shows", e => new ShowDto(Str(e, "year", "shows") ?? "", Str(e, "text", "shows") ?? "")),
        };
    }

    private static JsonElement RequireObject(JsonElement body) =>
        body.ValueKind == JsonValueKind.Object ? body : throw ProfileException.Validation("body", "must be a JSON object.");

    private static string? Str(JsonElement obj, string name, string? field = null)
    {
        if (!obj.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : throw ProfileException.Validation(field ?? name, "must be text.");
    }

    private static bool Bool(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static List<T>? List<T>(JsonElement obj, string name, Func<JsonElement, T> map)
    {
        if (!obj.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind != JsonValueKind.Array) throw ProfileException.Validation(name, "must be a list.");
        return v.EnumerateArray()
            .Select(e => e.ValueKind == JsonValueKind.Object ? map(e) : throw ProfileException.Validation(name, "has an invalid item."))
            .ToList();
    }
}

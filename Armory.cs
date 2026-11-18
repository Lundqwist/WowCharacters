using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace WowCharacters;

/// <summary>
/// Reads characters from Blizzard's official API (a client made at develop.battle.net: BLIZZARD_CLIENT_ID and
/// BLIZZARD_CLIENT_SECRET, OAuth2 client credentials). Retail and Forever use the same API with different namespaces.
/// What is read is kept in memory for an hour (CacheTime), so a page view rarely waits for Blizzard.
/// </summary>
public class Armory(IConfiguration config, ILogger<Armory> log)
{
    public static readonly TimeSpan CacheTime = TimeSpan.FromHours(1);
    /// <summary>How often the "Update from the Armory" button may empty a game's cache.</summary>
    public static readonly TimeSpan RefreshPause = TimeSpan.FromMinutes(2);
    const string Api = "https://eu.api.blizzard.com";
    const string Locale = "locale=en_GB";

    static readonly HttpClient Http = CreateClient();
    static readonly SemaphoreSlim TokenLock = new(1, 1);
    readonly ConcurrentDictionary<string, (DateTime At, object? Value)> _cache = new();
    // Per game: anything read before this time counts as old (set by the update button)
    readonly ConcurrentDictionary<string, DateTime> _staleBefore = new();
    static readonly object RefreshLock = new();

    string? _token;
    DateTime _tokenExpires;

    static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; LundqwistWowCharacters/1.0)");
        return http;
    }

    string ClientId => config["BLIZZARD_CLIENT_ID"] ?? "";
    string ClientSecret => config["BLIZZARD_CLIENT_SECRET"] ?? "";
    public bool Configured => ClientId.Length > 0 && ClientSecret.Length > 0;

    public static string ArmoryUrl(Game game, string key) =>
        game.ArmoryPrefix + string.Join('/', key.Split('/').Select(Uri.EscapeDataString));

    string ProfileUrl(Game game, string key, string part) =>
        $"{Api}/profile/wow/character/{string.Join('/', key.Split('/').Select(Uri.EscapeDataString))}{part}?namespace={game.ProfileNamespace}&{Locale}";

    /// <summary>The short version for the list page: name, level, class, race, picture. Null when it can't be read.</summary>
    /// <summary>
    /// Makes everything read for this game count as old, so the next page view reads it again from Blizzard.
    /// Allowed once per RefreshPause; returns how long is left to wait when it is too soon, otherwise null.
    /// Old values stay in the cache and are still shown if Blizzard can't be reached.
    /// </summary>
    public TimeSpan? Refresh(Game game)
    {
        lock (RefreshLock)
        {
            var now = DateTime.UtcNow;
            if (_staleBefore.TryGetValue(game.Key, out var last) && now - last < RefreshPause) return RefreshPause - (now - last);
            _staleBefore[game.Key] = now;
            return null;
        }
    }

    public Task<object?> Card(Game game, string key) => Cached(game, $"card:{game.Key}:{key}", async () =>
    {
        using var summary = await GetJson(ProfileUrl(game, key, ""));
        if (summary is null) return null;
        using var media = await GetJson(ProfileUrl(game, key, "/character-media"));
        var s = summary.RootElement;
        var pictures = Pictures(media);
        return new
        {
            name = FullName(s),
            level = Int(s, "level"),
            @class = Name(s, "character_class"),
            race = Name(s, "race"),
            spec = Name(s, "active_spec"),
            faction = Name(s, "faction"),
            realm = Name(s, "realm"),
            guild = Name(s, "guild"),
            itemLevel = Int(s, "equipped_item_level"),
            avatar = pictures.GetValueOrDefault("avatar", ""),
        };
    });

    /// <summary>Everything for the character's own page: summary, picture, gear, stats, professions.</summary>
    public Task<object?> Sheet(Game game, string key) => Cached(game, $"sheet:{game.Key}:{key}", async () =>
    {
        var docs = await Task.WhenAll(new[] { "", "/equipment", "/character-media", "/statistics", "/professions" }
            .Select(part => GetJson(ProfileUrl(game, key, part))));
        try
        {
            if (docs[0] is null) return null;
            var s = docs[0]!.RootElement;
            var pictures = Pictures(docs[2]);
            return new
            {
                name = FullName(s),
                title = s.TryGetProperty("active_title", out var t) ? Text(t, "display_string").Replace("{name}", FullName(s)) : "",
                level = Int(s, "level"),
                @class = Name(s, "character_class"),
                race = Name(s, "race"),
                gender = Name(s, "gender"),
                spec = Name(s, "active_spec"),
                faction = Name(s, "faction"),
                realm = Name(s, "realm"),
                guild = Name(s, "guild"),
                itemLevel = Int(s, "equipped_item_level"),
                achievementPoints = Int(s, "achievement_points"),
                lastLogin = s.TryGetProperty("last_login_timestamp", out var seen) && seen.TryGetInt64(out var ms) ? ms : 0,
                render = pictures.GetValueOrDefault("main-raw", pictures.GetValueOrDefault("main", "")),
                avatar = pictures.GetValueOrDefault("avatar", ""),
                gear = Gear(docs[1]),
                stats = Stats(docs[3]),
                professions = Professions(docs[4]),
            };
        }
        finally { foreach (var d in docs) d?.Dispose(); }
    });

    async Task<object?> Cached(Game game, string cacheKey, Func<Task<object?>> read)
    {
        if (!Configured) return null;
        var staleBefore = _staleBefore.GetValueOrDefault(game.Key, DateTime.MinValue);
        if (_cache.TryGetValue(cacheKey, out var kept) && DateTime.UtcNow - kept.At < CacheTime && kept.At >= staleBefore) return kept.Value;
        try
        {
            var value = await read();
            _cache[cacheKey] = (DateTime.UtcNow, value);
            return value;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            log.LogWarning("Could not read {Key} from Blizzard's API: {Message}", cacheKey, e.Message);
            // Show the last thing that was read, however old, rather than nothing
            return kept.Value;
        }
    }

    // ---- The parts of the sheet ----

    static Dictionary<string, string> Pictures(JsonDocument? media)
    {
        var pictures = new Dictionary<string, string>();
        if (media is not null && media.RootElement.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            foreach (var a in assets.EnumerateArray()) pictures[Text(a, "key")] = Text(a, "value");
        return pictures;
    }

    static List<object> Gear(JsonDocument? doc)
    {
        var gear = new List<object>();
        if (doc is null || !doc.RootElement.TryGetProperty("equipped_items", out var items) || items.ValueKind != JsonValueKind.Array) return gear;
        foreach (var item in items.EnumerateArray())
            gear.Add(new
            {
                slot = Name(item, "slot"),
                name = Text(item, "name"),
                quality = Name(item, "quality", "type"),
                id = Id(item, "item"),
                level = item.TryGetProperty("level", out var il) ? (int)Number(il, "value") : 0,
            });
        return gear;
    }

    static List<object> Stats(JsonDocument? doc)
    {
        var stats = new List<object>();
        if (doc is null) return stats;
        var s = doc.RootElement;
        void Add(string label, double value, bool percent = false, bool always = false)
        {
            if (value <= 0 && !always) return;
            stats.Add(new { label, value = percent ? value.ToString("0.0", CultureInfo.InvariantCulture) + "%" : Math.Round(value).ToString("#,0", CultureInfo.InvariantCulture) });
        }
        Add("Health", Number(s, "health"), always: true);
        var power = Name(s, "power_type");
        if (power.Length > 0) Add(power, Number(s, "power"), always: true);
        Add("Strength", Number(s, "strength"));
        Add("Agility", Number(s, "agility"));
        Add("Intellect", Number(s, "intellect"));
        Add("Stamina", Number(s, "stamina"), always: true);
        Add("Spirit", Number(s, "spirit"));
        Add("Armor", Number(s, "armor"), always: true);
        Add("Attack power", Number(s, "attack_power"));
        Add("Spell power", Number(s, "spell_power"));
        Add("Critical strike", Math.Max(Number(s, "melee_crit"), Math.Max(Number(s, "ranged_crit"), Number(s, "spell_crit"))), percent: true);
        Add("Haste", Math.Max(Number(s, "melee_haste"), Number(s, "spell_haste")), percent: true);
        Add("Mastery", Number(s, "mastery"), percent: true);
        Add("Versatility", Number(s, "versatility_damage_done_bonus"), percent: true);
        Add("Dodge", Number(s, "dodge"), percent: true);
        Add("Parry", Number(s, "parry"), percent: true);
        Add("Block", Number(s, "block"), percent: true);
        return stats;
    }

    static List<object> Professions(JsonDocument? doc)
    {
        var list = new List<object>();
        if (doc is null) return list;
        foreach (var group in new[] { "primaries", "secondaries" })
        {
            if (!doc.RootElement.TryGetProperty(group, out var professions) || professions.ValueKind != JsonValueKind.Array) continue;
            foreach (var p in professions.EnumerateArray())
            {
                var tiers = new List<object>();
                if (p.TryGetProperty("tiers", out var ts) && ts.ValueKind == JsonValueKind.Array)
                    foreach (var tier in ts.EnumerateArray())
                        tiers.Add(new { name = Name(tier, "tier"), skill = Int(tier, "skill_points"), max = Int(tier, "max_skill_points") });
                // Professions without tiers (cooking/fishing in some versions) give their skill directly
                else if (Int(p, "skill_points") > 0)
                    tiers.Add(new { name = "", skill = Int(p, "skill_points"), max = Int(p, "max_skill_points") });
                list.Add(new { name = Name(p, "profession"), primary = group == "primaries", tiers });
            }
        }
        return list;
    }

    // ---- Talking to the API ----

    async Task<string> Token()
    {
        await TokenLock.WaitAsync();
        try
        {
            if (_token is not null && DateTime.UtcNow < _tokenExpires) return _token;
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth.battle.net/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{ClientId}:{ClientSecret}")));
            using var response = await Http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Blizzard refused the API client ({(int)response.StatusCode}). Check BLIZZARD_CLIENT_ID and BLIZZARD_CLIENT_SECRET.");
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            _token = doc.RootElement.GetProperty("access_token").GetString()!;
            _tokenExpires = DateTime.UtcNow.AddSeconds(doc.RootElement.GetProperty("expires_in").GetInt32() - 300);
            return _token;
        }
        finally { TokenLock.Release(); }
    }

    /// <summary>GET with the API token. Null for 404 (no such character, or below level 10).</summary>
    async Task<JsonDocument?> GetJson(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await Token());
        using var response = await Http.SendAsync(request);
        if ((int)response.StatusCode == 404) return null;
        if ((int)response.StatusCode == 401) _token = null; // a new token next time
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    // A number that the API gives either plainly (health: 1272) or wrapped ({"effective": 47}, {"value": 4.9})
    static double Number(JsonElement e, string property)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(property, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
        if (v.ValueKind != JsonValueKind.Object) return 0;
        foreach (var inner in new[] { "effective", "value" })
            if (v.TryGetProperty(inner, out var n) && n.ValueKind == JsonValueKind.Number) return n.GetDouble();
        return 0;
    }

    // Forever characters have a surname too: "Fader Fudiop"
    static string FullName(JsonElement s) => $"{Text(s, "name")} {Text(s, "surname")}".Trim();

    static int Int(JsonElement e, string property) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;

    static string Text(JsonElement e, string property) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    // A text inside a nested object: {"character_class": {"name": "Priest"}}
    static string Name(JsonElement e, string property, string inner = "name") =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(property, out var v) ? Text(v, inner) : "";

    static int Id(JsonElement e, string property) =>
        e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.Object && v.TryGetProperty("id", out var id) && id.TryGetInt32(out var n) ? n : 0;
}

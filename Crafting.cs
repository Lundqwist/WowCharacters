using System.Text.Json;

namespace WowCharacters;

/// <summary>
/// The Forever professions shown on the crafting page (forever/crafting.html), and their recipe lists in
/// wwwroot/recipes/&lt;file&gt;.json (made once from Wowhead's Forever database, wowhead.com/forever).
/// Blizzard's API names the recipes a character knows; the page needs their spell ids, so this class turns
/// recipe names into spell ids with those files. (Moved here from the retired Jawbreakers guild site, 2026-11-20.)
/// </summary>
public class Crafting(IWebHostEnvironment env)
{
    public record Profession(string Name, string Guide, string Recipes);

    const string Guides = "https://www.wow-professions.com/forever/";

    /// <summary>In the order the page shows them. Recipes is the file name in wwwroot/recipes; empty for gathering.</summary>
    public static readonly Profession[] Professions =
    [
        new("Alchemy", Guides + "alchemy-leveling-guide", "alchemy"),
        new("Blacksmithing", Guides + "blacksmithing-leveling-guide", "blacksmithing"),
        new("Enchanting", Guides + "enchanting-leveling-guide", "enchanting"),
        new("Engineering", Guides + "engineering-leveling-guide", "engineering"),
        new("Leatherworking", Guides + "leatherworking-leveling-guide", "leatherworking"),
        new("Tailoring", Guides + "tailoring-leveling-guide", "tailoring"),
        new("Cooking", Guides + "cooking-leveling-guide", "cooking"),
        new("First Aid", Guides + "first-aid-leveling-guide", "first-aid"),
        new("Mining", Guides + "mining-leveling-guide", "mining"),
        new("Herbalism", Guides + "herbalism-leveling-guide", ""),
        new("Skinning", Guides + "skinning-leveling-guide", ""),
        new("Fishing", Guides + "fishing-leveling-guide", ""),
    ];

    Dictionary<string, Dictionary<string, List<int>>>? _spellIds;

    /// <summary>The spell ids of the recipes with these names (one name can be several recipes).</summary>
    public List<int> SpellIds(string profession, IEnumerable<string> recipeNames)
    {
        var all = _spellIds ??= ReadSpellIds();
        if (!all.TryGetValue(profession, out var byName)) return [];
        return recipeNames.SelectMany(n => byName.GetValueOrDefault(n, [])).Distinct().ToList();
    }

    Dictionary<string, Dictionary<string, List<int>>> ReadSpellIds()
    {
        var all = new Dictionary<string, Dictionary<string, List<int>>>();
        foreach (var p in Professions.Where(p => p.Recipes.Length > 0))
        {
            var file = Path.Combine(env.WebRootPath, "recipes", p.Recipes + ".json");
            if (!File.Exists(file)) continue;
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var byName = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in doc.RootElement.GetProperty("recipes").EnumerateArray())
            {
                var name = r.GetProperty("name").GetString() ?? "";
                if (!byName.TryGetValue(name, out var ids)) byName[name] = ids = [];
                ids.Add(r.GetProperty("id").GetInt32());
            }
            all[p.Name] = byName;
        }
        return all;
    }
}

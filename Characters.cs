using System.Text.Json;
using Markdig;

namespace WowCharacters;

/// <summary>The two games, each with its own pages and its own part of Blizzard's API.</summary>
public record Game(string Key, string Title, string ProfileNamespace, string StaticNamespace, string ArmoryPrefix, bool HasRealms)
{
    public static readonly Game Retail = new("retail", "World of Warcraft",
        "profile-eu", "static-eu",
        "https://worldofwarcraft.blizzard.com/en-gb/character/eu/", HasRealms: true);

    public static readonly Game Forever = new("forever", "World of Warcraft: Forever",
        "profile-forever-eu", "static-forever-eu",
        "https://worldofwarcraft.blizzard.com/en-gb/forever/eu/armory/character/", HasRealms: false);

    public static Game? Find(string key) =>
        key.Equals(Retail.Key, StringComparison.OrdinalIgnoreCase) ? Retail :
        key.Equals(Forever.Key, StringComparison.OrdinalIgnoreCase) ? Forever : null;
}

/// <summary>
/// One character as listed in data/characters.json.
///   Retail:  { "game": "retail",  "realm": "draenor", "name": "moonie" }
///   Forever: { "game": "forever", "name": "lunara-moonshade" }  (Forever has no realms; the name is the
///            Armory slug, "name-surname", as in the character's Armory address)
/// </summary>
public class CharacterEntry
{
    public string Game { get; set; } = "";
    public string Realm { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Optional: hide a character without removing its line.</summary>
    public bool Hidden { get; set; }

    /// <summary>
    /// The character's id in addresses and file names: "draenor/moonie" for retail, "lunara-moonshade" for Forever.
    /// Lower case, as Blizzard's API wants it.
    /// </summary>
    public string Key(Game game) => (game.HasRealms ? $"{Realm.Trim()}/{Name.Trim()}" : Name.Trim()).ToLowerInvariant();
}

/// <summary>Reads the character list and the stories from the data folder on every request, so edits show at once.</summary>
public class CharacterStore(string dataDir)
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    // Raw HTML in a story is shown as text, never run as part of the page
    static readonly MarkdownPipeline Markdown = new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();

    string ListFile => Path.Combine(dataDir, "characters.json");

    /// <summary>Creates an empty list with a short how-to the first time the site starts.</summary>
    public void EnsureCreated()
    {
        Directory.CreateDirectory(Path.Combine(dataDir, "stories", "retail"));
        Directory.CreateDirectory(Path.Combine(dataDir, "stories", "forever"));
        if (File.Exists(ListFile)) return;
        File.WriteAllText(ListFile, """
            // The characters shown on lundqwist.se/wowcharacters. Edit this file; changes show at once.
            //   Retail:  { "game": "retail",  "realm": "draenor", "name": "moonie" }
            //   Forever: { "game": "forever", "name": "lunamal-sunshadow", "realm": "PvP ruleset (EU)" }
            //            (name = the slug from the Armory address; Forever has no realms, so "realm" is only a label)
            // A story: a Markdown file in stories/retail/<realm>-<name>.md or stories/forever/<name>.md
            { "characters": [] }
            """);
    }

    public List<CharacterEntry> All(Game game)
    {
        if (!File.Exists(ListFile)) return [];
        using var stream = File.OpenRead(ListFile);
        var list = JsonSerializer.Deserialize<CharacterList>(stream, Options)?.Characters ?? [];
        return list.Where(c => !c.Hidden && c.Name.Trim().Length > 0 && Game.Find(c.Game) == game && (!game.HasRealms || c.Realm.Trim().Length > 0)).ToList();
    }

    public CharacterEntry? Find(Game game, string key) =>
        All(game).FirstOrDefault(c => c.Key(game) == key.ToLowerInvariant());

    string StoryFile(Game game, CharacterEntry c)
    {
        // The key comes from characters.json, not from the address, but is still kept to a plain file name
        var name = new string(c.Key(game).Replace('/', '-').Where(ch => char.IsLetterOrDigit(ch) || ch == '-').ToArray());
        return Path.Combine(dataDir, "stories", game.Key, name + ".md");
    }

    /// <summary>The story as HTML, or null when there is no story file yet.</summary>
    public string? Story(Game game, CharacterEntry c)
    {
        var file = StoryFile(game, c);
        return File.Exists(file) ? Markdig.Markdown.ToHtml(File.ReadAllText(file), Markdown) : null;
    }

    /// <summary>The story as Markdown, or "" when there is none yet.</summary>
    public string StoryMarkdown(Game game, CharacterEntry c)
    {
        var file = StoryFile(game, c);
        return File.Exists(file) ? File.ReadAllText(file) : "";
    }

    static readonly object WriteLock = new();

    /// <summary>
    /// Adds a chapter to the end of the story, or replaces the whole story. The version it replaces is kept
    /// in stories/&lt;game&gt;/.history/ first, so nothing posted can be lost.
    /// </summary>
    public string WriteStory(Game game, CharacterEntry c, string markdown, bool replace)
    {
        lock (WriteLock)
        {
            var file = StoryFile(game, c);
            var old = File.Exists(file) ? File.ReadAllText(file) : "";
            if (old.Length > 0)
            {
                var history = Path.Combine(Path.GetDirectoryName(file)!, ".history");
                Directory.CreateDirectory(history);
                File.WriteAllText(Path.Combine(history, $"{Path.GetFileNameWithoutExtension(file)}-{DateTime.Now:yyyyMMdd-HHmmss}.md"), old);
            }
            markdown = markdown.Replace("\r\n", "\n").Trim();
            var text = replace || old.Trim().Length == 0 ? markdown + "\n" : old.TrimEnd() + "\n\n" + markdown + "\n";
            File.WriteAllText(file, text, new System.Text.UTF8Encoding(false));
            return text;
        }
    }

    class CharacterList { public List<CharacterEntry> Characters { get; set; } = []; }
}

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
            // A story: one Markdown file per chapter in stories/retail/<realm>-<name>/ or stories/forever/<name>/ (001.md, 002.md, ...)
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

    // ---------------------------------------------------------------- stories
    //
    // A story is a row of chapters, one Markdown file each:  stories/<game>/<name>/001.md, 002.md, ...
    // Each starts with its title as a heading ("### The salt flats"), often followed by an italic line
    // (dates, zones). An older single file stories/<game>/<name>.md, if there is one, is shown before the
    // chapters as an introduction.

    // The key comes from characters.json, not from the address, but is still kept to a plain file name
    static string FileName(Game game, CharacterEntry c) =>
        new(c.Key(game).Replace('/', '-').Where(ch => char.IsLetterOrDigit(ch) || ch == '-').ToArray());

    string IntroFile(Game game, CharacterEntry c) => Path.Combine(dataDir, "stories", game.Key, FileName(game, c) + ".md");
    string ChapterDir(Game game, CharacterEntry c) => Path.Combine(dataDir, "stories", game.Key, FileName(game, c));
    string ChapterFile(Game game, CharacterEntry c, int n) => Path.Combine(ChapterDir(game, c), $"{n:000}.md");

    public record Chapter(int Number, string Title, string Subtitle, string Markdown);

    /// <summary>The introduction (the older single story file) as HTML, or null.</summary>
    public string? Intro(Game game, CharacterEntry c)
    {
        var file = IntroFile(game, c);
        var text = File.Exists(file) ? File.ReadAllText(file) : "";
        return text.Trim().Length > 0 ? Markdig.Markdown.ToHtml(text, Markdown) : null;
    }

    public List<Chapter> Chapters(Game game, CharacterEntry c)
    {
        var dir = ChapterDir(game, c);
        if (!Directory.Exists(dir)) return [];
        var list = new List<Chapter>();
        foreach (var file in Directory.GetFiles(dir, "*.md"))
        {
            if (!int.TryParse(Path.GetFileNameWithoutExtension(file), out var n) || n < 1) continue;
            list.Add(ParseChapter(n, File.ReadAllText(file)));
        }
        return list.OrderBy(x => x.Number).ToList();
    }

    static Chapter ParseChapter(int n, string text)
    {
        var lines = text.Replace("\r\n", "\n").Trim().Split('\n').ToList();
        string title = "", subtitle = "";
        if (lines.Count > 0 && lines[0].StartsWith('#'))
        {
            title = lines[0].TrimStart('#').Trim();
            lines.RemoveAt(0);
        }
        var first = lines.FindIndex(l => l.Trim().Length > 0);
        if (first >= 0)
        {
            var l = lines[first].Trim();
            if (l.Length > 2 && (l.StartsWith('*') && l.EndsWith('*') || l.StartsWith('_') && l.EndsWith('_')))
            {
                subtitle = l.Trim('*', '_').Trim();
                lines.RemoveAt(first);
            }
        }
        return new Chapter(n, title.Length > 0 ? title : $"Chapter {n}", subtitle, string.Join('\n', lines).Trim());
    }

    public string ChapterHtml(Chapter ch) => Markdig.Markdown.ToHtml(ch.Markdown, Markdown);

    public bool HasStory(Game game, CharacterEntry c) => Intro(game, c) is not null || Chapters(game, c).Count > 0;

    /// <summary>The whole story as Markdown (introduction and every chapter), for whoever writes the next chapter.</summary>
    public string StoryMarkdown(Game game, CharacterEntry c)
    {
        var parts = new List<string>();
        var intro = IntroFile(game, c);
        if (File.Exists(intro) && File.ReadAllText(intro).Trim().Length > 0) parts.Add(File.ReadAllText(intro).Trim());
        foreach (var ch in Chapters(game, c))
            parts.Add(File.ReadAllText(ChapterFile(game, c, ch.Number)).Replace("\r\n", "\n").Trim());
        return string.Join("\n\n", parts);
    }

    static readonly object WriteLock = new();

    /// <summary>
    /// Saves a chapter: a new one after the last (number null), or a new text for an existing one. A chapter that
    /// is replaced is kept in stories/&lt;game&gt;/.history/ first, so nothing posted can be lost. Returns its number.
    /// </summary>
    public int SaveChapter(Game game, CharacterEntry c, string markdown, int? number)
    {
        lock (WriteLock)
        {
            var dir = ChapterDir(game, c);
            Directory.CreateDirectory(dir);
            int n = number ?? (Chapters(game, c).Select(x => x.Number).DefaultIfEmpty(0).Max() + 1);
            var file = ChapterFile(game, c, n);
            if (File.Exists(file))
            {
                var history = Path.Combine(dataDir, "stories", game.Key, ".history");
                Directory.CreateDirectory(history);
                File.Copy(file, Path.Combine(history, $"{FileName(game, c)}-{n:000}-{DateTime.Now:yyyyMMdd-HHmmss}.md"));
            }
            File.WriteAllText(file, markdown.Replace("\r\n", "\n").Trim() + "\n", new System.Text.UTF8Encoding(false));
            return n;
        }
    }

    public bool ChapterExists(Game game, CharacterEntry c, int n) => File.Exists(ChapterFile(game, c, n));
    class CharacterList { public List<CharacterEntry> Characters { get; set; } = []; }
}

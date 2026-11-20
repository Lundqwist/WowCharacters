// My World of Warcraft characters, shown at https://lundqwist.se/wowcharacters/
//
//   /retail/                  the retail characters      (wwwroot/retail/index.html)
//   /forever/                 the WoW: Forever characters (wwwroot/forever/index.html)
//   /retail/character.html?c=draenor/moonie   one character: Armory information and its list of chapters
//   /forever/chapter.html?c=name-surname&n=2  one chapter of a character's story
//
// Which characters are shown: data/characters.json. Their stories: data/stories/<game>/<name>/001.md, 002.md...
// Both live in the data folder (Z:\wowcharacters\data on the NAS) and are never touched by deploys.
// The lundqwist.se site passes /wowcharacters/... on to this app, which runs with PathBase=/wowcharacters,
// so the pages only use relative addresses.

using WowCharacters;

var builder = WebApplication.CreateBuilder(args);

var dataDir = builder.Configuration["DataDir"] ?? Path.Combine(builder.Environment.ContentRootPath, "data");
var store = new CharacterStore(dataDir);
store.EnsureCreated();

builder.Services.AddSingleton(store);
builder.Services.AddSingleton<Armory>();
builder.Services.AddSingleton<Crafting>();

var app = builder.Build();

// lundqwist.se passes requests on over plain http and says in X-Forwarded-Proto that the visitor used https.
// Trusting it (only from the same machine, the default) keeps redirects such as /wowcharacters -> /wowcharacters/ on https.
app.UseForwardedHeaders(new ForwardedHeadersOptions { ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto });

var pathBase = app.Configuration["PathBase"];
if (!string.IsNullOrEmpty(pathBase)) app.UsePathBase(pathBase);

if (!app.Services.GetRequiredService<Armory>().Configured)
    app.Logger.LogWarning("BLIZZARD_CLIENT_ID / BLIZZARD_CLIENT_SECRET are not set: only the character names can be shown.");

// /retail -> /retail/ and so on, then the pages and their files
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache",
});

// Pictures and sounds in the stories: data/media/<game>/<name>/<file> at /media/<game>/<name>/<file>.
// They never change once uploaded (a new upload gets a new name), so they can be cached for long.
Directory.CreateDirectory(store.MediaRoot);
var mediaTypes = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
mediaTypes.Mappings[".ogg"] = "audio/ogg";
mediaTypes.Mappings[".oga"] = "audio/ogg";
app.UseStaticFiles(new StaticFileOptions
{
    RequestPath = "/media",
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(store.MediaRoot),
    ContentTypeProvider = mediaTypes,
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "public, max-age=604800",
});

// The characters of one game, for its list page
app.MapGet("/api/{game}/characters", async (string game, CharacterStore store, Armory armory) =>
{
    if (Game.Find(game) is not { } g) return Results.NotFound();
    var characters = store.All(g);
    var cards = await Task.WhenAll(characters.Select(c => armory.Card(g, c.Key(g))));
    return Results.Ok(new
    {
        game = g.Title,
        armory = armory.Configured,
        characters = characters.Select((c, i) => new
        {
            key = c.Key(g),
            name = c.Name,
            realm = c.Realm,
            armoryUrl = Armory.ArmoryUrl(g, c.Key(g)),
            hasStory = store.HasStory(g, c),
            chapters = store.Chapters(g, c).Count,
            card = cards[i],
        }),
    });
});

// One character: its Armory sheet and its story. Only characters in characters.json can be looked up.
app.MapGet("/api/{game}/character", async (string game, string c, CharacterStore store, Armory armory) =>
{
    if (Game.Find(game) is not { } g || store.Find(g, c) is not { } entry) return Results.NotFound();
    var key = entry.Key(g);
    return Results.Ok(new
    {
        game = g.Title,
        key,
        name = entry.Name,
        realm = entry.Realm,
        armoryUrl = Armory.ArmoryUrl(g, key),
        introHtml = store.Intro(g, entry),
        chapters = store.Chapters(g, entry).Select(ch => new { number = ch.Number, title = ch.Title, subtitle = ch.Subtitle }),
        sheet = await armory.Sheet(g, key),
    });
});

// The crafting page (Forever only): every character's professions, skill and the spell ids of the recipes
// it knows, from Blizzard's API. The recipe lists themselves are static files in wwwroot/recipes.
app.MapGet("/api/forever/crafting", async (CharacterStore store, Armory armory, Crafting crafting) =>
{
    var g = Game.Find("forever")!;
    var characters = store.All(g);
    var cards = await Task.WhenAll(characters.Select(c => armory.Card(g, c.Key(g))));
    var known = await Task.WhenAll(characters.Select(c => armory.Professions(g, c.Key(g))));
    var order = Crafting.Professions.Select((p, i) => (p.Name, i)).ToDictionary(x => x.Name, x => x.i);
    return Results.Ok(new
    {
        armory = armory.Configured,
        professions = Crafting.Professions.Select(p => new { name = p.Name, guide = p.Guide, recipes = p.Recipes }),
        characters = characters.Select((c, i) => new
        {
            key = c.Key(g),
            name = c.Name,
            card = cards[i],
            professions = (known[i] as List<Armory.KnownProfession>)?
                .Where(p => order.ContainsKey(p.Name))
                .OrderBy(p => order[p.Name])
                .Select(p => new { name = p.Name, skill = p.Skill, known = crafting.SpellIds(p.Name, p.Recipes) }),
        }),
    });
});

// The "Update from the Armory" button: the game's cached Armory information counts as old, so the page's
// next load reads it again from Blizzard. Anyone may press it, but only once per Armory.RefreshPause per game.
app.MapPost("/api/{game}/refresh", (string game, Armory armory) =>
{
    if (Game.Find(game) is not { } g) return Results.NotFound();
    if (!armory.Configured) return Results.Json(new { error = "Armory information is not available." }, statusCode: 503);
    if (armory.Refresh(g) is { } wait)
        return Results.Json(new { wait = (int)Math.Ceiling(wait.TotalSeconds) }, statusCode: 429);
    app.Logger.LogInformation("Armory information for {Game} marked for updating.", g.Key);
    return Results.Ok(new { ok = true });
});

// Writing stories from outside (the StoryMaker program, github.com/Lundqwist/StoryMaker).
// Off unless STORY_TOKEN is set; every call needs "Authorization: Bearer <STORY_TOKEN>".
//   GET  /api/{game}/story?c=<key>                         -> { markdown }: the whole story so far
//   POST /api/{game}/story?c=<key>  { markdown }             -> saves it as a new chapter: { chapter }
//   POST /api/{game}/story?c=<key>  { markdown, chapter: 2 } -> new text for chapter 2 (the old one is kept in .history)
var storyToken = app.Configuration["STORY_TOKEN"] ?? "";
if (storyToken.Length > 0 && storyToken.Length < 32)
{
    app.Logger.LogWarning("STORY_TOKEN is shorter than 32 characters: story writing is switched off.");
    storyToken = "";
}

bool Authorized(HttpRequest request)
{
    if (storyToken.Length == 0) return false;
    var header = request.Headers.Authorization.ToString();
    if (!header.StartsWith("Bearer ", StringComparison.Ordinal)) return false;
    var given = System.Text.Encoding.UTF8.GetBytes(header[7..].Trim());
    var wanted = System.Text.Encoding.UTF8.GetBytes(storyToken);
    return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(given, wanted);
}

app.MapGet("/api/{game}/story", (string game, string c, HttpRequest request, CharacterStore store) =>
{
    if (storyToken.Length == 0) return Results.NotFound();
    if (!Authorized(request)) return Results.Unauthorized();
    if (Game.Find(game) is not { } g || store.Find(g, c) is not { } entry) return Results.NotFound();
    return Results.Ok(new { key = entry.Key(g), markdown = store.StoryMarkdown(g, entry) });
});

app.MapPost("/api/{game}/story", (string game, string c, StoryPost body, HttpRequest request, CharacterStore store) =>
{
    if (storyToken.Length == 0) return Results.NotFound();
    if (!Authorized(request)) return Results.Unauthorized();
    if (Game.Find(game) is not { } g || store.Find(g, c) is not { } entry) return Results.NotFound();
    if (string.IsNullOrWhiteSpace(body.Markdown)) return Results.BadRequest(new { error = "No text." });
    if (body.Markdown.Length > 200_000) return Results.BadRequest(new { error = "Too long." });
    if (body.Chapter is { } wanted && !store.ChapterExists(g, entry, wanted))
        return Results.BadRequest(new { error = $"There is no chapter {wanted}." });
    var n = store.SaveChapter(g, entry, body.Markdown, body.Chapter);
    app.Logger.LogInformation("Story of {Game}/{Key}: chapter {Number} {What}.", g.Key, entry.Key(g), n, body.Chapter is null ? "added" : "replaced");
    return Results.Ok(new { key = entry.Key(g), chapter = n });
}).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(400_000));

// Pictures and sounds for the stories (StoryMaker uploads them):
//   POST /api/{game}/media?c=<key>  multipart "file"  -> { url }   (same token as the stories)
app.MapPost("/api/{game}/media", async (string game, string c, HttpRequest request, CharacterStore store) =>
{
    if (storyToken.Length == 0) return Results.NotFound();
    if (!Authorized(request)) return Results.Unauthorized();
    if (Game.Find(game) is not { } g || store.Find(g, c) is not { } entry) return Results.NotFound();
    if (!request.HasFormContentType) return Results.BadRequest(new { error = "Send the file as multipart/form-data." });
    var form = await request.ReadFormAsync();
    if (form.Files.GetFile("file") is not { Length: > 0 } file) return Results.BadRequest(new { error = "No file." });
    try
    {
        using var stream = file.OpenReadStream();
        var path = store.SaveMedia(g, entry, file.FileName, stream);
        app.Logger.LogInformation("Media for {Game}/{Key}: {Path} ({Length} bytes).", g.Key, entry.Key(g), path, file.Length);
        return Results.Ok(new { url = $"{request.PathBase}/media/{path}" });
    }
    catch (ArgumentException e)
    {
        return Results.BadRequest(new { error = e.Message });
    }
}).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(30_000_000));

// One chapter of a story, with the chapters before and after it
app.MapGet("/api/{game}/chapter", (string game, string c, int n, CharacterStore store) =>
{
    if (Game.Find(game) is not { } g || store.Find(g, c) is not { } entry) return Results.NotFound();
    var chapters = store.Chapters(g, entry);
    var i = chapters.FindIndex(ch => ch.Number == n);
    if (i < 0) return Results.NotFound();
    var ch = chapters[i];
    return Results.Ok(new
    {
        key = entry.Key(g),
        name = entry.Name,
        number = ch.Number,
        title = ch.Title,
        subtitle = ch.Subtitle,
        html = store.ChapterHtml(ch),
        count = chapters.Count,
        position = i + 1,
        previous = i > 0 ? new { number = chapters[i - 1].Number, title = chapters[i - 1].Title } : null,
        next = i < chapters.Count - 1 ? new { number = chapters[i + 1].Number, title = chapters[i + 1].Title } : null,
    });
});

app.Map("/api/{**rest}", () => Results.NotFound());

app.Run();

record StoryPost(string Markdown, int? Chapter = null);

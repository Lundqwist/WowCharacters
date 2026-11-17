// My World of Warcraft characters, shown at https://lundqwist.se/wowcharacters/
//
//   /retail/                  the retail characters      (wwwroot/retail/index.html)
//   /forever/                 the WoW: Forever characters (wwwroot/forever/index.html)
//   /retail/character.html?c=draenor/moonie   one character: Armory information and its story
//
// Which characters are shown: data/characters.json. Their stories: data/stories/<game>/*.md.
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
            hasStory = store.Story(g, c) is not null,
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
        storyHtml = store.Story(g, entry),
        sheet = await armory.Sheet(g, key),
    });
});

app.Map("/api/{**rest}", () => Results.NotFound());

app.Run();

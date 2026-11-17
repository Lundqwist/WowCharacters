# WowCharacters

My World of Warcraft characters, shown at <https://lundqwist.se/wowcharacters/>.

Retail and WoW: Forever are two separate places:

| Page | Address |
|---|---|
| Start (pick a game) | `/wowcharacters/` |
| Retail characters | `/wowcharacters/retail/` |
| WoW: Forever characters | `/wowcharacters/forever/` |
| One character | `/wowcharacters/retail/character.html?c=<realm>/<name>`, `/wowcharacters/forever/character.html?c=<name-surname>` |

Each character page shows what the Armory knows (picture, level, race, class, guild, item level, stats,
gear with Wowhead tooltips, professions), read live from Blizzard's official API and kept in memory for
an hour, and the character's story.

## Adding characters and stories

Everything lives in the data folder (`Z:\wowcharacters\data` on the NAS, `data\` when run locally),
which deploys never touch. Changes show at once; no restart needed.

`characters.json`:

```jsonc
{ "characters": [
  { "game": "retail",  "realm": "draenor", "name": "moonie" },
  { "game": "forever", "name": "lunamal-sunshadow", "realm": "PvP ruleset (EU)" },  // Forever: the slug from the Armory address; no realms, so "realm" is only a label
  { "game": "retail",  "realm": "draenor", "name": "oldalt", "hidden": true }
] }
```

Stories are Markdown files:

- retail: `stories/retail/<realm>-<name>.md`, e.g. `stories/retail/draenor-moonie.md`
- Forever: `stories/forever/<name>.md`, e.g. `stories/forever/lunara-moonshade.md`

Raw HTML in a story is shown as text. Images can be linked with Markdown (`![](https://...)`).

## How it runs

ASP.NET Core 8 minimal API (`Program.cs`, `Armory.cs`, `Characters.cs`) and plain HTML/JS pages in
`wwwroot`. On the NAS it runs as the `wowcharacters` container on `127.0.0.1:8096` with
`PathBase=/wowcharacters`; the lundqwist.se site ([Website2.0](https://github.com/Lundqwist/Website2.0))
passes `/wowcharacters/...` on to it, so the pages only use relative addresses.

Blizzard API client (develop.battle.net, OAuth2 client credentials): `BLIZZARD_CLIENT_ID` and
`BLIZZARD_CLIENT_SECRET`, in `Z:\wowcharacters\.env` (see `deploy\.env.example`). Without them only the
character names and stories are shown.

## Local run

```powershell
$env:BLIZZARD_CLIENT_ID = [Environment]::GetEnvironmentVariable('BLIZZARD_CLIENT_ID','User')
$env:BLIZZARD_CLIENT_SECRET = [Environment]::GetEnvironmentVariable('BLIZZARD_CLIENT_SECRET','User')
dotnet run -- --urls=http://localhost:8096 --PathBase=/wowcharacters
```

Then open <http://localhost:8096/wowcharacters/>.

## Deploy

```powershell
.\deploy.ps1
```

Then restart the `wowcharacters` project in DSM Container Manager (the first time: create the project
from `/volume1/docker/wowcharacters/docker-compose.yml`).

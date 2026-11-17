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

A story is a row of chapters, one Markdown file each, in a folder per character:

- retail: `stories/retail/<realm>-<name>/001.md`, `002.md`, ... e.g. `stories/retail/draenor-moonie/001.md`
- Forever: `stories/forever/<name>/001.md`, ... e.g. `stories/forever/lunamal-sunshadow/001.md`

A chapter starts with its title as a heading (`### Salt on the Wind`), optionally followed by an
italic line (`*17 November 2026 · Thousand Needles · level 41*`), then the text. The character page
lists the chapters with links; each chapter has its own page
(`forever/chapter.html?c=<name>&n=<number>`) with links to the previous and next chapter. An older
single file `stories/<game>/<name>.md`, if there is one, is shown above the list as an introduction.

Raw HTML in a story is shown as text. Pictures and sounds use Markdown's picture syntax; a picture on
a line of its own gets its alt text as a caption below it, and a "picture" whose file is a sound
(`.mp3`, `.ogg`, `.wav`, `.m4a`) becomes a sound player with its caption:

```markdown
![Ratchet at dawn](/wowcharacters/media/forever/lunamal-sunshadow/ratchet.jpg)
![The harbour bell](/wowcharacters/media/forever/lunamal-sunshadow/bell.mp3)
---
```

`---` is shown as a row of gold stars between scenes. Uploaded files live in
`data/media/<game>/<name>/` and are served at `/wowcharacters/media/...`.

### Posting stories from a program

The StoryMaker program ([LunarJournal](https://github.com/Lundqwist/LunarJournal)) writes chapters
from the in-game journal and posts them here. The API is off unless `STORY_TOKEN` (at least 32
characters) is set in `.env`; every call needs `Authorization: Bearer <STORY_TOKEN>`:

| | |
|---|---|
| `GET /api/{game}/story?c=<key>` | the whole story so far as Markdown: `{ key, markdown }` |
| `POST /api/{game}/story?c=<key>` with `{ "markdown": "..." }` | saves it as the next chapter: `{ key, chapter }` |
| `POST /api/{game}/story?c=<key>` with `{ "markdown": "...", "chapter": 2 }` | new text for chapter 2 |
| `POST /api/{game}/media?c=<key>` with a multipart `file` (pictures: jpg, png, webp, gif; sounds: mp3, ogg, wav, m4a; up to 30 MB) | stores it under a safe, unique name: `{ url }` |

Only characters in `characters.json` can be written. A chapter that is replaced is kept first in
`stories/<game>/.history/<name>-<number>-<date>.md`.
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

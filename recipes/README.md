# Readers

This folder holds Ur Score's **reader files**: small `.recipe.json` files that say which https
address to read, how to find the rows in the answer, and what each number is called. Players never
see them. They are built into Ur Score, and `games/*.game.json` wires them to modes (Pet Sim 99's
Battle and Profile are in `games/pet-sim-99.game.json`). There is no importing and no custom
readers: a reader changes by changing the file here and releasing a new Ur Score, so every
install gets the same text.

This page is for developers. The file format (steps, extraction, periods, inputs, stats, the host
fence) is written up in [docs/2026-09-13-recipes-design.md](../docs/2026-09-13-recipes-design.md).

All three read Big Games' public Pet Simulator 99 API, and none carries a key or a login.

| File | What it reads | Mode |
| --- | --- | --- |
| [`pet-sim-99-clan-battle.recipe.json`](pet-sim-99-clan-battle.recipe.json) | A clan's current battle: the clan's place and total, every contributor ranked, and your own accounts' points inside it. Every 3 minutes. Takes the clan's name as its one input. | Battle |
| [`pet-sim-99-top-clans.recipe.json`](pet-sim-99-top-clans.recipe.json) | The clans at the top of the current battle, with their points. Every 5 minutes. | Battle |
| [`pet-sim-99-profile.recipe.json`](pet-sim-99-profile.recipe.json) | Each of your own accounts' profile: rank, rebirths, eggs hatched, diamonds and the rest. Every 30 minutes. Takes no input; it reads the accounts saved in RoRoRo. | Profile |

## Trying one against the live source

```powershell
626labs.ur-score.exe --try recipes/pet-sim-99-profile.recipe.json --account <robloxUserId> --json
```

`--try` runs one reader file against its real source and prints what Ur Score would have
extracted, with `--input id=value` for an input (the clan battle's `clan`), `--stat key` to pick
stats and `--account` for a Roblox user id.

## Adding or changing a reader

1. Edit or add the `.recipe.json` here. It is embedded at build (`recipes\*.recipe.json`).
2. Name its slug in a mode's `reads` in a `games/*.game.json`. A reader no mode names is never
   read (`ManifestTests` checks the slugs, the `board` key and the `asks` input).
3. A changed reader starts a new version in each player's score book, which is multi-version by
   design: old lines keep the text they were read with and the series stays continuous.

A mode's `note`, `blurb` and the hosts line on the game page come from the manifest and the
reader files, so a new host in a reader shows up in Setup without any UI change.

## Two things worth knowing

- **These files are data, not code.** You can open any of them in Notepad and read every address it
  will contact. Ur Score adds nothing of its own to a reader's requests but its User-Agent,
  `UrScore/<version> (RoRoRo plugin)`, and `NoHostnameFenceTests` keeps hostnames out of the code.
- **The profile reader needs each account linked.** Profile numbers come from a Big Games account
  page that has to be linked on db.biggames.io with its Profile view set to public. An account
  that isn't linked reads as empty, and Ur Score says why next to the dashes; that sentence is the
  reader's own. The reader also reads when Big Games last fetched a profile and whether their copy
  is stale, so a number that is hours old says so.

Data from Big Games' public Pet Simulator 99 API. Ur Score is not made by, endorsed by, or
affiliated with Big Games or Roblox.

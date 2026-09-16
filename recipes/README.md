# Recipes

Ur Score knows no game of its own. A recipe is a small `.recipe.json` file that says which https
address to read, how to find the rows in the answer, and what each number is called. Ur Score runs
it and knows nothing else.

These two are the ones the clan uses. Both read Big Games' public Pet Simulator 99 API, and neither
carries a key or a login.

| File | What it reads | What you need to fill in |
| --- | --- | --- |
| [`pet-sim-99-clan-battle.recipe.json`](pet-sim-99-clan-battle.recipe.json) | A clan's current battle: the clan's place and total, every contributor ranked, and your own accounts' points inside it. Reads every 3 minutes. | Your clan's name, exactly as it appears in game. Watch for letters that look like digits — `K0i2` and `Koi2` are two different clans. |
| [`pet-sim-99-profile.recipe.json`](pet-sim-99-profile.recipe.json) | Each of your own accounts' profile: rank, rebirths, eggs hatched, diamonds and the rest. Reads every 30 minutes. | Nothing. It reads the accounts you have saved in RoRoRo. |

## How to add one

In Ur Score: **Setup → Recipes → Import**, and give it the file or its URL. The review screen lists
every host the recipe will make your PC contact, and what each one receives, before anything is
added. Then pick your clan under **Setup → Clans**.

## The profile one needs each account linked

Profile numbers come from a Big Games account page that has to be **linked on db.biggames.io with
its Profile view set to public**. An account that isn't linked reads as empty — Ur Score shows
dashes and says why, right next to them. That is the recipe's own sentence, not Ur Score's, and it
is the whole fix: link the account, and the numbers appear on the next read.

## Two things worth knowing

- **These files are data, not code.** You can open either in Notepad and read every address it will
  contact. Nothing is hidden, and Ur Score adds nothing of its own to a recipe's requests but its
  User-Agent.
- **Big Games' profile data has its own age.** The profile recipe reads when Big Games last fetched
  a profile and whether their copy is stale, so a number that is hours old says so rather than
  pretending to be current.

Data from Big Games' public Pet Simulator 99 API. Ur Score is not made by, endorsed by, or
affiliated with Big Games or Roblox.

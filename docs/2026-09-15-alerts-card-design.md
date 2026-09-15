# An Alerts page that tells you what to do

Approved by the owner on 2026-09-15. Target: Ur Score v0.3.2, before the clan battle on Saturday 2026-09-19. Branch `feat/alerts-card` off master (v0.3.1 plus the backlog docs).

## Why

On 0.3.1, Setup › Alerts has one "Add this rule to RoRoRo" button per sent stat.
- Its confirmation is a stock Windows box that shows raw JSON (`metricId`, `kind`, `threshold`, `windowMinutes`, `alertWhenBelow`, `owner`).
- The rule is fixed: under 100 a minute over 10 minutes.
- After the click the button just turns grey; the only sign of success is a terse "Ready: RoRoRo has a rule for clan.battle.points at 100."
- Nothing says that alerts still need Metric alerts turned on in RoRoRo, with destinations.

The owner found it: "that is easy, we need it to be more clear about what to do."

## What the owner sees

Every stat you send gets a card on Setup › Alerts. Its alerts are sentences in your words:

- **Stops climbing:** "Alert me when an account's **Points** gains fewer than **100** a minute for **10** minutes." The number is editable; the minutes are a choice of 10, 15 or 30.
- **Crosses a number:** "Alert me when an account's **Rank** goes **above / below** **40**."

On each card:

- **+ Add an alert** asks which kind, then shows its sentence with the parts to fill in, and **Turn on**.
- Each alert Ur Score added shows **Change** and **Remove**. A stat may have one of each kind (RoRoRo allows several rules per metric).
- Rules you wrote yourself, or that another plugin owns, are listed as sentences too, marked "yours" or "another plugin's", with no Change or Remove. Ur Score never edits them.
- After Turn on, Change or Remove, the card says what happened, in the theme: "On. RoRoRo will alert you when an account's Points gains fewer than 100 a minute for 10 minutes."
- Under the cards, one standing line: "Next, in RoRoRo: Settings › Alerts › turn on Metric alerts and choose where they go (desktop, Discord, phone)." Ur Score can't read RoRoRo's settings, so this line is always shown while at least one alert is on.
- No JSON on screen, and no stock message box. A failure to write the file (it's locked, unreadable, or not a list) is said on the card, themed, and changes nothing.

## Rules file (RoRoRo's `%LOCALAPPDATA%\ROROROblox\metric-rules.json`)

- **Writes keep everything else.** Ur Score still keeps every rule it doesn't own, backs the file up before each write, and refuses to touch a file that isn't valid JSON.
- **Kinds Ur Score writes:** `Rate` (stops climbing: `threshold` per minute, `windowMinutes`) and `Level` (crosses a number: `threshold`, `alertWhenBelow`). `Event` isn't offered.
- **Owner and label.** Every rule Ur Score writes carries `"owner": "626labs.ur-score"` and a new `"label"`: the stat's label from the recipe (e.g. "Points", "Diamonds"). RoRoRo 1.28 ignores unknown fields, so writing `label` is safe today. The companion RoRoRo change reads it to word alerts.
- **Removing** deletes exactly the one rule Ur Score owns for that metric and kind; **Change** rewrites it in place.
- **Existing rules.** The rule 0.3.1 added (owner `626labs.ur-score`, no label) is recognised as Ur Score's. The next Change adds its label.

## Rules that still bind

- Every popup and toast is themed.
- Every `src/UI` list is a `ui:RowList`.
- DynamicResource brushes only.
- No hostname literals in `src`.
- Copy is sentence case, second person, no emoji, and Ur Score's own text never names a game; stat labels come from the recipe.
- Other players never reach disk.
- `ReportPolicy.SendAsync` stays the only path to RoRoRo.
- Merging and releasing v0.3.2 each wait for the owner's OK.

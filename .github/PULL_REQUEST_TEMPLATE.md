<!--
Delete any section that doesn't apply. a docs or wording change
shouldn't have to answer questions about Harmony patches etc...
-->

## Brief Overview

<!-- One or two sentences. What did you do overall? -->

## Why

<!--
Explain why the changes were made:
 - If this is a bugfix, say what was wrong and how you know it's resolved.
 - If it's a whole new feature, explain in detail.
 - If this is a refactor explain why it is better than the previous code.
 etc...
-->

## Type of change

- [ ] Docs, README or wording only
- [ ] C# mod (`Archipelago.CultOfTheLamb/`)
- [ ] Python apworld (`worlds/cult_of_the_lamb/`)
- [ ] Both halves - see the version note below

---

## If you touched the C# mod

- [ ] `dotnet build Archipelago.CultOfTheLamb.sln --configuration Release` succeeds
- [ ] Deployed to a r2modman profile and **launched the game**. The mod loads with no
      new errors in `LogOutput.log`

## If you touched the Python apworld

- [ ] A seed generates
- [ ] Tests pass: `py -3.12 -m unittest discover -s worlds/cult_of_the_lamb/test -t .`
- [ ] `py -3.12 build_apworld.py` rebuilds cleanly


### YAML options

- [ ] **No existing option slot name changed.** Renaming one silently breaks every player's
      config
- [ ] New options are implemented as a YAML choice rather than narrowed to one behaviour,
      and `rules.py` models each combination honestly
- [ ] `cult_of_the_lamb_example.yaml` updated if options changed


---

## Anything else

<!--
Known gaps. things you deliberately left out, anything you're unsure about.
-->

# AI disclosure

Provided as required by the Archipelago developer disclosure policy.

**Was AI used?** Yes

## Tool

Claude Code (Anthropic).

## What it is used for

- **Research.** Almost everything here is derived from a decompile of the game's assembly.
  Claude was used to build an extensive index into all the game's systems and files that were decompiled to
  help make feature research and development faster and easier.
- **Coding/Comments** Designs are built out by hand (interfaces, class design, algorithms) but a good deal of
  method implementation is left to AI.
- **Log analysis** when diagnosing bug reports.
- **Repetitive consistency work**, such as keeping location names and IDs matching between the C#
  mod and the Python apworld.

## How extensively

A decent share of both the C# mod and the Python apworld are drafted with AI rather
than typed by hand.

Code comments are heavily AI-generated, to subpar quality. I have been and will continue rewriting and deleting them over time, to be more useful to me and to any future collaborators.


## What it is not used for

- **No AI art.** The mod icon and all visuals are my own work or the game's own assets.
- **Design decisions are mine.** The option set, the check economy, the goal conditions, and what
  does and does not belong in the mod.
- **Release notes, changelogs and Discord posts** are written by me.

## Human review process

All code is reviewed by me before anything lands in main. Nothing ships without me reading it, testing it in an actual gameplay session, and fixing it where needed. I direct the work, I understand how this mod works
and why each part of it is there, and I can answer questions about any of it. AI is used to speed up development
of my projects in a way that aligns with my best moral judgement and industry best practice.

Code review is not a formality. AI tools misread game behaviour, propose fixes for the wrong
cause, and suggest designs I reject for reasons it had not considered. No AI tool replaces deep human understanding
and subject matter expertise to make a project successful.

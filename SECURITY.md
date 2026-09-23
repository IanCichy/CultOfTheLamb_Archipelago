# Security

This is a single-player game mod. It runs locally under BepInEx and talks to whatever Archipelago
server you point it at. It has no accounts or credentials of its own, and stores nothing beyond two
text files in your BepInEx config folder.

There's no formal disclosure process. If you find something that looks like a real security issue
rather than a bug, open an issue and mark it clearly. If it's something you'd rather not write up
in public first, message me in the Cult of the Lamb thread on the
[Archipelago Discord](https://discord.gg/archipelago) and we'll sort out where to take it.

The Archipelago connection is a plain websocket to a server address you choose, and the password
you set is passed straight through to that server. Treat a server address someone hands you with
the same caution you'd treat any other.

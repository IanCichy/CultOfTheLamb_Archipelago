# Security

This is a single-player game mod. It runs locally under BepInEx and talks to whatever Archipelago
server you point it at. It has no accounts or credentials of its own, and stores nothing beyond a
few files next to your saves in your BepInEx profile folder.

There's no formal disclosure process. If you find something that looks like a real security issue
rather than a bug, open an issue and mark it clearly, or contact the maintainer directly.

Note that the Archipelago connection itself is a plain websocket to a server address you choose,
and the password you set is passed straight through to that server. Treat a server address someone
hands you with the same caution you'd treat any other.

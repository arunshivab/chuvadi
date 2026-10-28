# Interoperability results (1.0.0, verified in the build sandbox)

Executed by `InteropTests` (they pass silently when a tool is missing, so CI on any OS stays green).

| Direction | Peer | Cases |
|---|---|---|
| Chuvadi writes, peer reads | Info-ZIP `unzip -t` 6.00 | seekable, Zip64 always, data descriptors, descriptors + Zip64, stored |
| Chuvadi writes, peer reads | Python `zipfile` (testzip + SHA-256 of content) | same five variants, UTF-8 names, comments, empty entries, directories |
| Chuvadi writes, peer reads | pyzipper 0.4.0 | AES-128/192/256 x AE-1/AE-2 x seekable/descriptor, non-ASCII password |
| Peer writes, Chuvadi reads | Python `zipfile` | deflate, stored, directory, forced Zip64, comment, Unix mode, Unicode name |
| Peer writes, Chuvadi reads | pyzipper | AES-128/192/256, wrong-password detection |
| Peer writes, Chuvadi reads | Info-ZIP `zip` 3.0 | recursive tree, ZipCrypto (`-P`), forced Zip64 (`-fz`); Info-ZIP unzip extracts a tree Chuvadi zipped |

Not testable here: 7-Zip and WinRAR (not installed), Windows Explorer. Please open `tests` output archives with them on Windows as the visual check.

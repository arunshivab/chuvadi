# Interoperability results (1.0.0)

Two sets of results: the automated checks run in the build sandbox (`InteropTests`), and the checks Arun ran by hand on Windows.

## Automated (`InteropTests`)

They pass silently when a tool is missing, so CI on any OS stays green.

| Direction | Peer | Cases |
|---|---|---|
| Chuvadi writes, peer reads | Info-ZIP `unzip -t` 6.00 | seekable, Zip64 always, data descriptors, descriptors + Zip64, stored |
| Chuvadi writes, peer reads | Python `zipfile` (testzip + SHA-256 of content) | same five variants, UTF-8 names, comments, empty entries, directories |
| Chuvadi writes, peer reads | pyzipper 0.4.0 | AES-128/192/256 x AE-1/AE-2 x seekable/descriptor, non-ASCII password |
| Peer writes, Chuvadi reads | Python `zipfile` | deflate, stored, directory, forced Zip64, comment, Unix mode, Unicode name |
| Peer writes, Chuvadi reads | pyzipper | AES-128/192/256, wrong-password detection |
| Peer writes, Chuvadi reads | Info-ZIP `zip` 3.0 | recursive tree, ZipCrypto (`-P`), forced Zip64 (`-fz`); Info-ZIP unzip extracts a tree Chuvadi zipped |

The AES wrong-password check tries up to eight wrong passwords per archive. AES checks a password with a 2-byte verifier derived from a random salt, so one fixed wrong password would, about 1 time in 65,536, pass the verifier and fail later with `ZipIntegrityException`. The test accepts that outcome for a candidate and requires that at least one candidate is rejected with `ZipPasswordRequiredException` (`WrongPassword` true).

## Checked by hand on Windows (Arun, Windows 11, 5 Nov 2026)

The archive is the fixed ZipCrypto test archive (two entries, password `hunter2`). The wrong-password cases are the two fixed passwords from `ZipCryptoPasswordTests`: `wrong-password-0` fails ZipCrypto's one-byte check, `wrong-password-200051` passes it and then yields garbage.

| Password | Entry | 7-Zip | Windows Explorer (Compressed Folders) |
|---|---|---|---|
| `hunter2` | stored.txt | opens; text "Stored entry: the quick brown fox jumps over the lazy dog." | not tested |
| `wrong-password-0` (fails the check byte) | stored.txt | "Wrong password : stored.txt" | "The password that you typed is not correct. Try typing it again." |
| `wrong-password-200051` (passes the check byte) | stored.txt | "CRC failed : Wrong password? : stored.txt" | "Windows cannot complete the extraction. The destination file could not be created." |
| `wrong-password-200051` | deflated.txt | "Data error : Wrong password? : deflated.txt" | the same file-creation error |

What this shows:

- **7-Zip** reports a check-byte collision as "password or damage", with the question mark. Chuvadi.Zip does the same: `ZipPasswordRequiredException` with `WrongPassword` true, and the CRC or decompression failure as `InnerException` (D-177, option C).
- **Windows Explorer** gives a misleading file-creation error for the collision. That is Explorer's behaviour, not something a library can change.
- No code change followed from these results.

## Not yet checked

WinRAR, macOS Archive Utility and Windows Explorer with an AES-encrypted archive have not been tried. Please open archives produced by the `tests` output with them where you can, as the visual check.

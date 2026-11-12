# D-345 — black-box statement on ZWJ and ZWNJ (from the Pdf chat, 2026-11-11)

Received through the library coordinator. Observed output only (rule 2): HarfBuzz through uharfbuzz 0.57.0, the same
reference as the corpus, run as a black box with LiPi Sans (Latin, Devanagari, Malayalam, Tamil) and Liberation Sans,
cluster levels 0, 1 and 2. No source was read. Kept here as the basis for decision E3(b) (D-345, Arun: ZWJ joins the
cluster before it; ZWNJ starts its own), which the engine implements in R-TS-0031.

Statement, as received:

- Neither joiner disappears; each becomes the font's space glyph with zero advance unless a ligature takes it.
- Level 0 (the default, and the level the corpus uses):
  - ZWJ joins the cluster before it, whatever that is (letter, mark sequence, whole conjunct, space, even a ZWNJ).
    The line `z e r o ZWJ w i d t h ZWNJ j o i n` gives ZWJ cluster 3 and ZWNJ its own cluster 10.
  - `a ZWNJ ZWJ b`: ZWJ joins the ZWNJ's cluster 1. `a ZWJ ZWJ b`: both cluster 0.
  - Devanagari ksha ZWJ ka: ZWJ joins cluster 0.
  - ZWNJ always keeps its own cluster (`a ZWJ ZWNJ b`: ZWNJ is 2; ka + virama + ZWNJ + ssa: ZWNJ is 2; the same for
    Tamil and Malayalam).
  - The character after a joiner is never merged into it.
  - A joiner at the start gets its own cluster.
  - A virama before ZWNJ merges into its base's cluster (cluster 0).
- Levels 1 and 2: both joiners keep their own index (ZWJ 4, ZWNJ 10); the ZWJ backward merge is level 0 only; a virama
  before ZWNJ keeps its own index 1.
- When the font forms a ligature with the joiner there is no joiner glyph; the ligature carries the first cluster
  (ra + virama + ZWJ + ya gives the eyelash reph at cluster 0, then ya at cluster 3; Malayalam na + virama + ZWJ gives
  one chillu glyph at cluster 0).
- Half forms: ka + virama + ZWJ + ssa gives the half form plus a zero-width ZWJ glyph (cluster 0), then ssa at
  cluster 3; Tamil the same.

How the engine relates to it (phase 1, default path):

- Clusters follow level 0: ZWJ joins the cluster before it, ZWNJ and the character after a joiner start their own,
  a joiner at the start has its own (R-TS-0031). The contract's clusters are level-0-like (D-66), so levels 1 and 2
  are not offered.
- Both joiners become the space glyph with zero advance after GSUB unless a substitution consumed them (E8, R-TS-0029).
- The Indic cases (virama, half forms, reph, chillu) are phase 2 and 3 work; they are recorded here for then.

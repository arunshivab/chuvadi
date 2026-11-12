# Black-box statement on fallback marks: narrow bases and the horn (from a separate test chat, 2026-11-12)

Received through the library coordinator, in substance as relayed, for the 4 corpus lines still differing after
D-354 (claude/d354-fallback-measurement.md) and the horn. Observed output only (rule 2): HarfBuzz 15.0.0 through
uharfbuzz 0.57.0, run as a black box on ChuvadiTest-Cff.otf (SHA-256
7f30cb9e4b7e9394088d5cee4ef9bbc78f04d7f7656dbb4250950643e4ba8258). No source was read. The tester's own file with the
full numbers is fallback-mark-positioning-blackbox.md (not filed here).

It corrects d345-fallback-statement.md on one point: the horizontal reference is the base's advance, not its ink box.

## Statement, as received

- The three expected values come out exactly: f + U+0308 = −410, f + U+0301 = −410, i + U+0301 = −370.
- Narrow bases have no rule of their own. The earlier wide-base rule needs correcting: the mark is centred on the base's
  ADVANCE, not its ink box. The wide-base tests (A, H, x, W) could not tell the two apart because their ink is centred
  in their advance. B settles it: ink centre 280, advance centre 290, and the mark lands centred at 290.
- One formula fits every base tried: x_offset = −advance/2 − 250 for this mark. No minimum offset and no clamping; on
  f and i the mark hangs past the left side of the glyph.
- Vertical: above-marks sit 62 units above the base's ink top (752 on i, which is 690 tall); a second stacked mark
  sits 62 above the first; the 62 scales with font size.
- The horn U+031B is −450 on every base (A, f, i and the four controls): minus the mark's own ink right edge, so the
  horn's right edge lines up with the end of the base's advance. Its bottom touches the base's top with no gap
  (y = 700, or 690 on i). The earlier −450 holds whatever the base.
- Caveats: the font has no glyph for any combining mark, so every mark became .notdef (400 wide); whether the 62 or the
  horn rule changes with the mark's size could not be tested. On the f_i ligature the mark's offset is −385, which does
  not fit the rule and probably attaches to the last ligature component (not analysed).

## How the engine relates to it (R-TS-0037)

- Centring is on the base's advance: x = (the base's start) + advance/2 − (the mark's ink centre). For an odd advance
  the half is rounded toward zero; the statement does not settle that.
- The horn (216) puts the mark's ink right edge at the end of the base's advance. The other right-attached classes
  (222, 226, 232) use the same reference and the left classes (200, 218, 224, 228) the start of the advance; that is by
  analogy only, the statement covers 216 alone.
- Vertical placement stays on the base's ink, as both statements say.
- The f_i ligature case is not handled specially; it is not in the corpus.

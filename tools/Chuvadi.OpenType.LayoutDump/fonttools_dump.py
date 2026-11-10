"""Canonical dump of GDEF, GSUB and GPOS made with fontTools, run as a black box (D-206).

Writes the same lines as the C# LayoutDump tool so the two sorted files can be compared with diff.
Usage: python -I fonttools_dump.py <font> <out.txt>     (needs: pip install fonttools brotli)
Test tool only; nothing shipped references fontTools.
"""
import sys

from fontTools.ttLib import TTFont

lines = []
font = None
gid = None
n = 0


def add(line):
    lines.append(line)


def join(values):
    return ",".join(str(v) for v in values)


def cov_glyphs(cov):
    return [gid(g) for g in (cov.glyphs if cov is not None else [])]


def classes(cd):
    out = {}
    if cd is None:
        return out
    for name, c in cd.classDefs.items():
        if c != 0:
            out[gid(name)] = c
    return out


def val(v):
    if v is None:
        return "0,0,0,0"
    return "%d,%d,%d,%d" % (getattr(v, "XPlacement", 0) or 0, getattr(v, "YPlacement", 0) or 0,
                            getattr(v, "XAdvance", 0) or 0, getattr(v, "YAdvance", 0) or 0)


def anchor(a):
    if a is None:
        return "-"
    if a.Format == 2:
        return "%d,%d,p%d" % (a.XCoordinate, a.YCoordinate, a.AnchorPoint)
    return "%d,%d" % (a.XCoordinate, a.YCoordinate)


def langsys(name, script, lang, ls):
    req = ls.ReqFeatureIndex
    add("%s script %s lang %s req %d feat %s" % (name, script.strip(), lang, -1 if req == 0xFFFF else req, join(ls.FeatureIndex)))


def records(recs):
    return ",".join("%d:%d" % (r.SequenceIndex, r.LookupListIndex) for r in (recs or []))


def dump_context(p, st, chained, is_gsub):
    fmt = st.Format
    rec_name = "SubstLookupRecord" if is_gsub else "PosLookupRecord"
    if fmt == 3:
        if chained:
            groups = [("bt", st.BacktrackCoverage), ("in", st.InputCoverage), ("la", st.LookAheadCoverage)]
        else:
            groups = [("bt", []), ("in", st.Coverage), ("la", [])]
        for label, covs in groups:
            for i, c in enumerate(covs):
                for g in cov_glyphs(c):
                    add("%s ctx3 %s %d %d" % (p, label, i, g))
        add("%s ctx3 rec %s" % (p, records(getattr(st, rec_name))))
        return
    for g in cov_glyphs(st.Coverage):
        add("%s cov %d" % (p, g))
    if fmt == 2:
        if chained:
            for g, c in classes(st.InputClassDef).items():
                add("%s cdin %d %d" % (p, g, c))
            for g, c in classes(st.BacktrackClassDef).items():
                add("%s cdbt %d %d" % (p, g, c))
            for g, c in classes(st.LookAheadClassDef).items():
                add("%s cdla %d %d" % (p, g, c))
        else:
            for g, c in classes(st.ClassDef).items():
                add("%s cdin %d %d" % (p, g, c))
    if is_gsub:
        base = {(False, 1): ("SubRuleSet", "SubRule"), (False, 2): ("SubClassSet", "SubClassRule"),
                (True, 1): ("ChainSubRuleSet", "ChainSubRule"), (True, 2): ("ChainSubClassSet", "ChainSubClassRule")}
    else:
        base = {(False, 1): ("PosRuleSet", "PosRule"), (False, 2): ("PosClassSet", "PosClassRule"),
                (True, 1): ("ChainPosRuleSet", "ChainPosRule"), (True, 2): ("ChainPosClassSet", "ChainPosClassRule")}
    set_name, rule_name = base[(chained, fmt)]
    cov = cov_glyphs(st.Coverage)
    for si, rs in enumerate(getattr(st, set_name) or []):
        if rs is None:
            continue
        key = ("glyph %d" % cov[si]) if fmt == 1 else ("class %d" % si)
        for k, r in enumerate(getattr(rs, rule_name) or []):
            def conv(seq):
                return [gid(x) for x in seq] if fmt == 1 else list(seq)
            bt = conv(r.Backtrack) if chained else []
            inp = conv(r.Input) if fmt == 1 else list(r.Input if chained else r.Class)
            la = conv(r.LookAhead) if chained else []
            add("%s ctx%d %s %d bt %s in %s la %s rec %s" % (p, fmt, key, k, join(bt), join(inp), join(la),
                                                               records(getattr(r, rec_name))))


def main():
    global font, gid, n
    font = TTFont(sys.argv[1], lazy=False)
    gid = font.getGlyphID
    n = len(font.getGlyphOrder())
    if "GDEF" in font:
        t = font["GDEF"].table
        for g, c in classes(t.GlyphClassDef).items():
            if 1 <= c <= 4:
                add("GDEF class %d %d" % (g, c))
        for g, c in classes(getattr(t, "MarkAttachClassDef", None)).items():
            add("GDEF markattach %d %d" % (g, c))
        mgs = getattr(t, "MarkGlyphSetsDef", None)
        if mgs is not None:
            for i, c in enumerate(mgs.Coverage):
                for g in cov_glyphs(c):
                    add("GDEF markset %d %d" % (i, g))
    for name in ("GSUB", "GPOS"):
        if name not in font:
            continue
        t = font[name].table
        for sr in t.ScriptList.ScriptRecord if t.ScriptList else []:
            if sr.Script.DefaultLangSys is not None:
                langsys(name, sr.ScriptTag, "dflt", sr.Script.DefaultLangSys)
            for lr in sr.Script.LangSysRecord:
                langsys(name, sr.ScriptTag, lr.LangSysTag.strip(), lr.LangSys)
        for i, fr in enumerate(t.FeatureList.FeatureRecord if t.FeatureList else []):
            add("%s feature %d %s lookups %s" % (name, i, fr.FeatureTag.strip(), join(fr.Feature.LookupListIndex)))
        for i, lk in enumerate(t.LookupList.Lookup if t.LookupList else []):
            mfs = getattr(lk, "MarkFilteringSet", None)
            add("%s lookup %d type %d flag %d mfs %d sub %d" % (name, i, lk.LookupType, lk.LookupFlag,
                                                                 -1 if mfs is None else mfs, len(lk.SubTable)))
            for j, st in enumerate(lk.SubTable):
                ltype = lk.LookupType
                if (name == "GSUB" and ltype == 7) or (name == "GPOS" and ltype == 9):
                    ltype = st.ExtensionLookupType
                    st = st.ExtSubTable
                p = "%s L%d.%d t%d" % (name, i, j, ltype)
                if name == "GSUB":
                    dump_gsub(p, ltype, st)
                else:
                    dump_gpos(p, ltype, st)
    lines.sort()
    with open(sys.argv[2], "w", encoding="utf-8", newline="\n") as f:
        f.write("".join(l + "\n" for l in lines))
    print("%s: %d lines" % (sys.argv[1], len(lines)))


def dump_gsub(p, t, st):
    if t == 1:
        for a, b in st.mapping.items():
            add("%s single %d %d" % (p, gid(a), gid(b)))
    elif t == 2:
        for a, seq in st.mapping.items():
            add("%s multiple %d %s" % (p, gid(a), join(gid(x) for x in seq)))
    elif t == 3:
        for a, alts in st.alternates.items():
            add("%s alternate %d %s" % (p, gid(a), join(gid(x) for x in alts)))
    elif t == 4:
        for first, ligs in st.ligatures.items():
            for k, lig in enumerate(ligs):
                add("%s ligature %d %d %s %d" % (p, gid(first), k, join(gid(x) for x in lig.Component), gid(lig.LigGlyph)))
    elif t == 5:
        dump_context(p, st, False, True)
    elif t == 6:
        dump_context(p, st, True, True)
    elif t == 8:
        for g, s in zip(cov_glyphs(st.Coverage), st.Substitute):
            add("%s rev cov %d sub %d" % (p, g, gid(s)))
        for i, c in enumerate(st.BacktrackCoverage):
            for g in cov_glyphs(c):
                add("%s rev bt %d %d" % (p, i, g))
        for i, c in enumerate(st.LookAheadCoverage):
            for g in cov_glyphs(c):
                add("%s rev la %d %d" % (p, i, g))


def dump_gpos(p, t, st):
    if t == 1:
        glyphs = cov_glyphs(st.Coverage)
        for i, g in enumerate(glyphs):
            v = st.Value if st.Format == 1 else st.Value[i]
            add("%s single %d %s" % (p, g, val(v)))
    elif t == 2 and st.Format == 1:
        for g1, ps in zip(cov_glyphs(st.Coverage), st.PairSet):
            for r in ps.PairValueRecord:
                add("%s pair %d %d %s %s" % (p, g1, gid(r.SecondGlyph), val(r.Value1), val(r.Value2)))
    elif t == 2 and st.Format == 2:
        glyphs = cov_glyphs(st.Coverage)
        for g in glyphs:
            add("%s pcov %d" % (p, g))
        c1 = classes(st.ClassDef1)
        c2 = classes(st.ClassDef2)
        for g, c in c1.items():
            if g < n:
                add("%s pc1 %d %d" % (p, g, c))
        for g, c in c2.items():
            if g < n:
                add("%s pc2 %d %d" % (p, g, c))
        reps1 = sorted(set(c1.get(g, 0) for g in glyphs))
        reps2 = sorted(set(c2.get(g, 0) for g in range(n)))
        for a in reps1:
            for b in reps2:
                if a >= st.Class1Count or b >= st.Class2Count:
                    continue
                rec = st.Class1Record[a].Class2Record[b]
                v1, v2 = val(rec.Value1), val(rec.Value2)
                if v1 != "0,0,0,0" or v2 != "0,0,0,0":
                    add("%s pcell %d %d %s %s" % (p, a, b, v1, v2))
    elif t == 3:
        for g, r in zip(cov_glyphs(st.Coverage), st.EntryExitRecord):
            add("%s cursive %d entry %s exit %s" % (p, g, anchor(r.EntryAnchor), anchor(r.ExitAnchor)))
    elif t in (4, 5, 6):
        if t == 6:
            marks, targets, marr = st.Mark1Coverage, st.Mark2Coverage, st.Mark1Array
        else:
            marks, targets, marr = st.MarkCoverage, (st.BaseCoverage if t == 4 else st.LigatureCoverage), st.MarkArray
        for g, r in zip(cov_glyphs(marks), marr.MarkRecord):
            add("%s mark %d %d %s" % (p, g, r.Class, anchor(r.MarkAnchor)))
        tg = cov_glyphs(targets)
        if t == 4:
            for g, r in zip(tg, st.BaseArray.BaseRecord):
                for c in range(st.ClassCount):
                    add("%s base %d class %d %s" % (p, g, c, anchor(r.BaseAnchor[c])))
        elif t == 6:
            for g, r in zip(tg, st.Mark2Array.Mark2Record):
                for c in range(st.ClassCount):
                    add("%s base %d class %d %s" % (p, g, c, anchor(r.Mark2Anchor[c])))
        else:
            for g, la in zip(tg, st.LigatureArray.LigatureAttach):
                for k, comp in enumerate(la.ComponentRecord):
                    for c in range(st.ClassCount):
                        add("%s lig %d comp %d class %d %s" % (p, g, k, c, anchor(comp.LigatureAnchor[c])))
    elif t == 7:
        dump_context(p, st, False, False)
    elif t == 8:
        dump_context(p, st, True, False)


if __name__ == "__main__":
    main()

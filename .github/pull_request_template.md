## Summary

<!-- One-paragraph description of what this PR changes. -->


## Motivation

<!-- Why is this change needed? Link to BACKLOG entry or issue if relevant. -->


## Type of change

- [ ] Feature (new module or new capability in an existing module)
- [ ] Bug fix (CHANGE-LOG.md gets a new A-entry if behavioural)
- [ ] Refactor (no behavioural change)
- [ ] Documentation only
- [ ] Build/CI/tooling

## Testing

- [ ] `dotnet build` passes with zero warnings
- [ ] `dotnet test` passes (all tests green)
- [ ] `python3 tools/check_style.py <changed-files>` passes
- [ ] New tests added for new behaviour (or rationale for not adding)
- [ ] If this touches Redaction: byte-level test that the redacted string is absent (BASELINE B15)
- [ ] If this touches the object-graph rewriter (Forms, Redaction, Watermark, Annotations): PreloadAllObjects called before iteration (BASELINE B16)

## Documentation

- [ ] `docs/CHANGE-LOG.md` — new A-entry if architectural or behavioural
- [ ] `docs/BASELINE.md` — new B-entry if a new invariant is introduced
- [ ] `docs/SESSION-STATE.md` — test counts / module list current
- [ ] `docs/BACKLOG.md` — item moved out of backlog if shipped
- [ ] `README.md` — capability table updated if user-visible

## Self-review checklist

- [ ] No `var` in `src/` (IDE0008)
- [ ] Braces on all control flow (IDE0011)
- [ ] All public params validated against null (CA1062)
- [ ] `<paramref>` only in method-level docs, not class `<remarks>` (CS1734)
- [ ] Using alias declared upfront where `System.IO` overlaps a Chuvadi namespace
- [ ] Test csproj has no `Version=` attributes (central package management)

## Related

<!-- Closes #N, related to #M, supersedes #L, etc. -->

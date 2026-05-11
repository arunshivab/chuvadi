#!/usr/bin/env python3
"""
Generate API reference Markdown from C# XML doc comments.

Scans src/Chuvadi.Pdf.*/**/*.cs, parses class/struct/enum/interface
declarations and their XML doc comments, and writes one Markdown file per
public type to docs/api/<Module>/<TypeName>.md.

Generated files are NOT human-edited — regenerate via:
    python3 tools/gen_api_docs.py
"""

from __future__ import annotations

import os
import re
import sys
from dataclasses import dataclass, field
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parent.parent
SRC_DIR = REPO_ROOT / "src"
OUT_DIR = REPO_ROOT / "docs" / "api"


# ── Models ────────────────────────────────────────────────────────────────


@dataclass
class Member:
    kind: str           # "property", "method", "field", "ctor", "enum_value", "event"
    name: str
    signature: str      # full signature line, single-spaced
    summary: str = ""
    parameters: list[tuple[str, str]] = field(default_factory=list)  # (name, doc)
    returns: str = ""
    remarks: str = ""
    is_static: bool = False


@dataclass
class TypeDecl:
    name: str
    kind: str           # "class", "struct", "enum", "interface", "record"
    namespace: str
    module: str         # short module name e.g. "Annotations"
    summary: str = ""
    remarks: str = ""
    is_abstract: bool = False
    is_sealed: bool = False
    is_static: bool = False
    base_types: list[str] = field(default_factory=list)
    members: list[Member] = field(default_factory=list)
    file_path: Path = None


# ── Parsing ───────────────────────────────────────────────────────────────


# Doc-comment line: /// optional whitespace then either a tag-bearing line or content
DOC_LINE = re.compile(r'^\s*///\s?(.*)$')

# A type declaration: optional modifiers, then "class|struct|enum|interface|record",
# then name (may include : base list)
TYPE_DECL = re.compile(
    r'^\s*public\s+'
    r'(?P<modifiers>(?:(?:abstract|sealed|static|partial|readonly|ref)\s+)*)'
    r'(?P<kind>class|struct|enum|interface|record(?:\s+(?:class|struct))?)\s+'
    r'(?P<name>[A-Z][A-Za-z0-9_]*)'
    r'(?:\s*<[^>]+>)?'      # generic params
    r'(?:\s*:\s*(?P<bases>[^{]+))?'
    r'\s*[{]?\s*$'
)

# Member: looks for public methods, properties, fields with their signature
# Accepts multiline; we'll only match the first line of the declaration.
MEMBER_DECL = re.compile(
    r'^\s*public\s+'
    r'(?P<modifiers>(?:(?:static|virtual|abstract|override|sealed|readonly|const|new|async|extern)\s+)*)'
    r'(?P<rest>[^{;]+?)'
    r'(?P<terminator>[{;]|\)\s*$)'
)

# Enum value: an identifier optionally followed by = value, then comma or close brace
ENUM_VALUE = re.compile(r'^\s*(?P<name>[A-Z][A-Za-z0-9_]*)\s*(?:=\s*[^,}]+)?\s*[,}]?\s*$')

NAMESPACE_DECL = re.compile(r'^\s*namespace\s+(?P<ns>[^;{]+)[;{]?\s*$')


def parse_doc_block(lines: list[str]) -> dict[str, list[str] | list[tuple[str, str]]]:
    """Parse a /// doc-comment block into structured fields."""
    out: dict = {"summary": [], "remarks": [], "params": [], "returns": []}
    current = "summary"
    param_name = None
    for raw in lines:
        m = DOC_LINE.match(raw)
        if not m:
            continue
        text = m.group(1).rstrip()

        # Tag handling
        if text.startswith("<summary>"):
            current = "summary"
            text = text[len("<summary>"):]
        if text.endswith("</summary>"):
            text = text[: -len("</summary>")]

        if text.startswith("<remarks>"):
            current = "remarks"
            text = text[len("<remarks>"):]
        if text.endswith("</remarks>"):
            text = text[: -len("</remarks>")]

        if text.startswith("<returns>"):
            current = "returns"
            text = text[len("<returns>"):]
        if text.endswith("</returns>"):
            text = text[: -len("</returns>")]

        # <param name="x">desc</param>
        pm = re.match(r'<param\s+name="(?P<n>[^"]+)">(?P<d>.*?)(?:</param>)?$', text)
        if pm:
            param_name = pm.group("n")
            param_desc = pm.group("d")
            out["params"].append((param_name, param_desc.strip()))
            current = "param"
            continue
        if text.endswith("</param>"):
            text = text[: -len("</param>")]

        cleaned = text.strip()
        if not cleaned:
            continue
        if current in ("summary", "remarks", "returns"):
            out[current].append(cleaned)
        elif current == "param" and param_name and out["params"]:
            # Append to last param's description
            n, d = out["params"][-1]
            out["params"][-1] = (n, (d + " " + cleaned).strip())

    return {
        "summary": " ".join(out["summary"]).strip(),
        "remarks": " ".join(out["remarks"]).strip(),
        "params":  out["params"],
        "returns": " ".join(out["returns"]).strip(),
    }


def clean_xml_refs(text: str) -> str:
    """Convert <see cref="X"/> to plain X, strip <c></c>, etc."""
    if not text:
        return ""
    text = re.sub(r'<see\s+cref="(?:[A-Z]:)?([^"]+)"\s*/>', r'`\1`', text)
    text = re.sub(r'<see\s+langword="([^"]+)"\s*/>', r'`\1`', text)
    text = re.sub(r'<paramref\s+name="([^"]+)"\s*/>', r'`\1`', text)
    text = re.sub(r'<c>([^<]+)</c>', r'`\1`', text)
    text = re.sub(r'<para>', '\n\n', text)
    text = re.sub(r'</para>', '', text)
    # <list>/<item> → markdown bullets
    text = re.sub(r'<list[^>]*>', '\n', text)
    text = re.sub(r'</list>', '', text)
    text = re.sub(r'<item>', '\n- ', text)
    text = re.sub(r'</item>', '', text)
    text = re.sub(r'<description>', '', text)
    text = re.sub(r'</description>', '', text)
    # <code> blocks
    text = re.sub(r'<code>', '\n```\n', text)
    text = re.sub(r'</code>', '\n```\n', text)
    # Collapse triple newlines down to two
    text = re.sub(r'\n{3,}', '\n\n', text)
    return text.strip()


def parse_file(path: Path) -> list[TypeDecl]:
    """Extract all public type declarations and their members from a .cs file."""
    text = path.read_text(encoding="utf-8")
    lines = text.split("\n")

    # Find namespace
    namespace = ""
    for line in lines:
        m = NAMESPACE_DECL.match(line)
        if m:
            namespace = m.group("ns").strip()
            break

    types: list[TypeDecl] = []
    i = 0
    while i < len(lines):
        # Accumulate doc-comment lines preceding a declaration
        doc_lines: list[str] = []
        while i < len(lines) and DOC_LINE.match(lines[i]):
            doc_lines.append(lines[i])
            i += 1

        if i >= len(lines):
            break

        # Try type declaration
        type_match = TYPE_DECL.match(lines[i])
        if type_match:
            doc = parse_doc_block(doc_lines)
            modifiers = type_match.group("modifiers") or ""
            kind = type_match.group("kind").split()[0]
            bases_raw = type_match.group("bases") or ""
            bases = [b.strip() for b in bases_raw.split(",") if b.strip()] if bases_raw else []

            module = namespace.replace("Chuvadi.Pdf.", "").split(".")[0]

            td = TypeDecl(
                name=type_match.group("name"),
                kind=kind,
                namespace=namespace,
                module=module,
                summary=clean_xml_refs(doc["summary"]),
                remarks=clean_xml_refs(doc["remarks"]),
                is_abstract="abstract" in modifiers,
                is_sealed="sealed" in modifiers,
                is_static="static" in modifiers,
                base_types=bases,
                file_path=path,
            )

            # Parse members within this type's body
            body_start = i
            brace_depth = 0
            seen_open = False
            j = i
            while j < len(lines):
                for ch in lines[j]:
                    if ch == "{":
                        brace_depth += 1
                        seen_open = True
                    elif ch == "}":
                        brace_depth -= 1
                if seen_open and brace_depth == 0:
                    break
                j += 1

            # j is the line containing the closing brace of the type body
            td.members = parse_members(lines[i + 1 : j], kind)
            types.append(td)
            i = j + 1
            continue

        i += 1

    return types


def parse_members(body_lines: list[str], type_kind: str) -> list[Member]:
    """Parse public members within a type body."""
    members: list[Member] = []

    if type_kind == "enum":
        # Enum values: identifiers possibly with /// doc above
        i = 0
        while i < len(body_lines):
            doc_lines: list[str] = []
            while i < len(body_lines) and DOC_LINE.match(body_lines[i]):
                doc_lines.append(body_lines[i])
                i += 1
            if i >= len(body_lines):
                break
            ev = ENUM_VALUE.match(body_lines[i])
            if ev and not body_lines[i].strip().startswith("//"):
                doc = parse_doc_block(doc_lines)
                members.append(Member(
                    kind="enum_value",
                    name=ev.group("name"),
                    signature=body_lines[i].strip().rstrip(",}").strip(),
                    summary=clean_xml_refs(doc["summary"]),
                ))
            i += 1
        return members

    # Class/struct/interface/record: parse members
    i = 0
    nested_depth = 0
    while i < len(body_lines):
        line = body_lines[i]
        # Track nested braces so we don't dive into nested type bodies
        # (we only emit top-level members of THIS type)
        for ch in line:
            if ch == "{":
                nested_depth += 1
            elif ch == "}":
                nested_depth -= 1

        # Accumulate doc-comment lines preceding a public-member line
        if DOC_LINE.match(line):
            doc_lines: list[str] = []
            while i < len(body_lines) and DOC_LINE.match(body_lines[i]):
                doc_lines.append(body_lines[i])
                i += 1
            if i >= len(body_lines):
                break
            line = body_lines[i]
        else:
            doc_lines = []

        # Only top-level (nested_depth == 0 or 1 — the type body itself)
        if nested_depth > 1:
            i += 1
            continue

        mm = MEMBER_DECL.match(line)
        if mm:
            modifiers = mm.group("modifiers") or ""
            rest = mm.group("rest").strip()
            terminator = mm.group("terminator")

            # Skip if this looks like a nested type
            if re.match(r'^(class|struct|enum|interface|record)\s', rest):
                i += 1
                continue

            # Identify member kind
            doc = parse_doc_block(doc_lines)
            sig = (modifiers + rest).strip()

            # Determine member type
            ends_with_close_paren = terminator.strip().endswith(")")
            if "(" in rest or ends_with_close_paren:
                # Method or constructor
                # `rest` may not contain the closing paren if it landed in terminator;
                # use the whole concatenation for parsing the name
                full = (rest + terminator).strip()
                paren_idx = full.index("(") if "(" in full else len(full)
                tokens_before_paren = full[:paren_idx].strip().split()
                if not tokens_before_paren:
                    # Malformed or non-member match; skip
                    i += 1
                    continue
                name_part = tokens_before_paren[-1]
                is_ctor = False
                # Constructors: identifier matches the type name and has no return
                # We'll approximate: if name_part has no preceding type token, it's a ctor
                # Simpler: check if the tokens before the paren are exactly 1 identifier
                # OR check from outside (caller knows type name)
                kind = "method"
                # Naive: if name_part matches typical type-name pattern AND there's only
                # one token before "(", it's likely a constructor. We resolve precisely
                # in render time when we know the type name.
                # For now, store as method; caller fixes.
                # Reconstruct full signature including the (possibly multi-line) param list.
                # If terminator is `)` (param list ends the line), use rest+terminator.
                if ends_with_close_paren:
                    full_sig = (modifiers + rest + terminator).strip()
                else:
                    full_sig = sig.rstrip(";").rstrip("{").strip()
                members.append(Member(
                    kind=kind,
                    name=name_part,
                    signature=full_sig,
                    summary=clean_xml_refs(doc["summary"]),
                    parameters=[(n, clean_xml_refs(d)) for n, d in doc["params"]],
                    returns=clean_xml_refs(doc["returns"]),
                    remarks=clean_xml_refs(doc["remarks"]),
                    is_static="static" in modifiers,
                ))
            elif "{" in line or terminator == "{":
                # Property
                tokens = rest.rstrip("{").strip().split()
                if tokens:
                    name = tokens[-1]
                    members.append(Member(
                        kind="property",
                        name=name,
                        signature=sig.rstrip("{").strip(),
                        summary=clean_xml_refs(doc["summary"]),
                        is_static="static" in modifiers,
                    ))
            elif terminator == ";" and "=>" in rest:
                # Expression-bodied property
                name_part = rest.split("=>")[0].strip().split()[-1]
                members.append(Member(
                    kind="property",
                    name=name_part,
                    signature=sig.rstrip(";").strip(),
                    summary=clean_xml_refs(doc["summary"]),
                    is_static="static" in modifiers,
                ))
            elif terminator == ";":
                # Field or constant
                tokens = rest.rstrip(";").strip().split()
                if "=" in rest:
                    name_idx = rest.index("=")
                    name_tokens = rest[:name_idx].strip().split()
                    if name_tokens:
                        name = name_tokens[-1]
                    else:
                        i += 1
                        continue
                elif tokens:
                    name = tokens[-1]
                else:
                    i += 1
                    continue
                members.append(Member(
                    kind="field",
                    name=name,
                    signature=sig.rstrip(";").strip(),
                    summary=clean_xml_refs(doc["summary"]),
                    is_static="static" in modifiers,
                ))

        i += 1

    return members


# ── Rendering ─────────────────────────────────────────────────────────────


def render_type(t: TypeDecl) -> str:
    """Render a TypeDecl to Markdown."""
    out: list[str] = []

    # Title
    kind_label = {
        "class": "Class",
        "struct": "Struct",
        "enum": "Enum",
        "interface": "Interface",
        "record": "Record",
    }.get(t.kind, "Type")

    modifiers = []
    if t.is_static:
        modifiers.append("static")
    elif t.is_abstract:
        modifiers.append("abstract")
    elif t.is_sealed:
        modifiers.append("sealed")
    modifier_str = " ".join(modifiers) + " " if modifiers else ""

    out.append(f"# {t.name}")
    out.append("")
    out.append(f"**{kind_label}** in `{t.namespace}` ({t.module})")
    out.append("")

    if t.summary:
        out.append(t.summary)
        out.append("")

    # Declaration
    out.append("```csharp")
    bases = f" : {', '.join(t.base_types)}" if t.base_types else ""
    out.append(f"public {modifier_str}{t.kind} {t.name}{bases}")
    out.append("```")
    out.append("")

    if t.remarks:
        out.append("## Remarks")
        out.append("")
        out.append(t.remarks)
        out.append("")

    # Enum values
    if t.kind == "enum":
        out.append("## Values")
        out.append("")
        out.append("| Name | Description |")
        out.append("|---|---|")
        for m in t.members:
            desc = m.summary or "—"
            out.append(f"| `{m.name}` | {desc} |")
        out.append("")
    else:
        # Group members
        ctors    = [m for m in t.members if m.kind == "method" and m.name == t.name]
        methods  = [m for m in t.members if m.kind == "method" and m.name != t.name]
        props    = [m for m in t.members if m.kind == "property"]
        fields   = [m for m in t.members if m.kind == "field"]

        if ctors:
            out.append("## Constructors")
            out.append("")
            for m in ctors:
                out.append(f"### `{m.signature}`")
                out.append("")
                if m.summary:
                    out.append(m.summary)
                    out.append("")
                if m.parameters:
                    out.append("**Parameters**")
                    out.append("")
                    for n, d in m.parameters:
                        out.append(f"- `{n}` — {d or '_(undocumented)_'}")
                    out.append("")

        if props:
            out.append("## Properties")
            out.append("")
            for m in props:
                static = "_static_ " if m.is_static else ""
                out.append(f"### `{m.name}`")
                out.append("")
                if static:
                    out.append(f"_{static.strip()}_")
                    out.append("")
                out.append(f"```csharp")
                out.append(m.signature)
                out.append(f"```")
                out.append("")
                if m.summary:
                    out.append(m.summary)
                    out.append("")

        if methods:
            out.append("## Methods")
            out.append("")
            for m in methods:
                static = "_static_ " if m.is_static else ""
                out.append(f"### `{m.name}`")
                out.append("")
                if static:
                    out.append(f"_{static.strip()}_")
                    out.append("")
                out.append(f"```csharp")
                out.append(m.signature)
                out.append(f"```")
                out.append("")
                if m.summary:
                    out.append(m.summary)
                    out.append("")
                if m.parameters:
                    out.append("**Parameters**")
                    out.append("")
                    for n, d in m.parameters:
                        out.append(f"- `{n}` — {d or '_(undocumented)_'}")
                    out.append("")
                if m.returns:
                    out.append(f"**Returns:** {m.returns}")
                    out.append("")
                if m.remarks:
                    out.append("**Remarks:** " + m.remarks)
                    out.append("")

        if fields:
            out.append("## Fields")
            out.append("")
            for m in fields:
                static = "_static_ " if m.is_static else ""
                out.append(f"### `{m.name}`")
                out.append("")
                if static:
                    out.append(f"_{static.strip()}_")
                    out.append("")
                out.append(f"```csharp")
                out.append(m.signature)
                out.append(f"```")
                out.append("")
                if m.summary:
                    out.append(m.summary)
                    out.append("")

    # Footer
    out.append("---")
    out.append("")
    rel_src = t.file_path.relative_to(REPO_ROOT).as_posix() if t.file_path else "?"
    out.append(f"_Source: [`{rel_src}`](../../../{rel_src})_")
    out.append(f"_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._")
    out.append("")

    return "\n".join(out)


def render_index(types_by_module: dict[str, list[TypeDecl]]) -> str:
    """Render the top-level API index."""
    out: list[str] = []
    out.append("# API Reference")
    out.append("")
    out.append("Auto-generated from XML doc comments. One file per public type, grouped by module.")
    out.append("")
    out.append("Regenerate with:")
    out.append("")
    out.append("```bash")
    out.append("python tools/gen_api_docs.py")
    out.append("```")
    out.append("")

    for module in sorted(types_by_module.keys()):
        types = sorted(types_by_module[module], key=lambda t: t.name)
        out.append(f"## Chuvadi.Pdf.{module}")
        out.append("")
        out.append("| Type | Kind | Description |")
        out.append("|---|---|---|")
        for t in types:
            kind = t.kind
            # First-sentence extraction. Take everything up to the first ". " followed by
            # a capital letter (rough sentence break), but bail out if that's the whole string.
            summary = (t.summary or "").strip()
            m = re.search(r"\.\s+[A-Z]", summary)
            if m:
                summary = summary[:m.start() + 1]
            # Truncate at first newline too
            summary = summary.split("\n")[0]
            out.append(f"| [{t.name}]({module}/{t.name}.md) | {kind} | {summary or '—'} |")
        out.append("")

    return "\n".join(out)


# ── Main ──────────────────────────────────────────────────────────────────


def main() -> int:
    if not SRC_DIR.is_dir():
        print(f"error: {SRC_DIR} not found", file=sys.stderr)
        return 1

    # Clean output directory (preserve git-tracked README if user added one)
    if OUT_DIR.exists():
        for p in OUT_DIR.rglob("*.md"):
            p.unlink()

    types_by_module: dict[str, list[TypeDecl]] = {}
    file_count = 0
    type_count = 0

    for cs_file in sorted(SRC_DIR.rglob("*.cs")):
        # Skip generated and obj/bin output
        rel = cs_file.relative_to(REPO_ROOT).as_posix()
        if "/bin/" in rel or "/obj/" in rel:
            continue
        file_count += 1
        types = parse_file(cs_file)
        for t in types:
            # Re-classify constructors (methods whose name matches the type name)
            for m in t.members:
                if m.kind == "method" and m.name == t.name:
                    m.kind = "method"  # rendered under Constructors section below
            types_by_module.setdefault(t.module, []).append(t)
            type_count += 1

    OUT_DIR.mkdir(parents=True, exist_ok=True)

    for module, types in types_by_module.items():
        mod_dir = OUT_DIR / module
        mod_dir.mkdir(parents=True, exist_ok=True)
        for t in types:
            md_path = mod_dir / f"{t.name}.md"
            md_path.write_text(render_type(t), encoding="utf-8")

    # Top-level index
    (OUT_DIR / "README.md").write_text(render_index(types_by_module), encoding="utf-8")

    print(f"Scanned {file_count} files, emitted {type_count} type pages "
          f"across {len(types_by_module)} modules.")
    return 0


if __name__ == "__main__":
    sys.exit(main())

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Chuvadi.Sheets.Internal;

/// <summary>
/// ISO/IEC 29500 Strict support. A Strict workbook uses the same element and attribute
/// names as a Transitional one but different namespace URIs (http://purl.oclc.org/ooxml/...).
/// The readers are written against the Transitional names; this class maps Strict namespaces
/// onto them. Only names are mapped: attribute and text values are never changed (except a
/// graphicData <c>uri</c>, which names a namespace).
/// </summary>
internal static class StrictOoxml
{
    internal const string RelationshipsNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    internal const string RelationshipsStrictNs = "http://purl.oclc.org/ooxml/officeDocument/relationships";
    internal const string MainStrictNs = "http://purl.oclc.org/ooxml/spreadsheetml/main";

    /// <summary>Strict namespace → Transitional namespace.</summary>
    private static readonly Dictionary<string, string> Map = new(StringComparer.Ordinal)
    {
        [MainStrictNs] = "http://schemas.openxmlformats.org/spreadsheetml/2006/main",
        [RelationshipsStrictNs] = RelationshipsNs,
        ["http://purl.oclc.org/ooxml/drawingml/main"] = "http://schemas.openxmlformats.org/drawingml/2006/main",
        ["http://purl.oclc.org/ooxml/drawingml/spreadsheetDrawing"] = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing",
        ["http://purl.oclc.org/ooxml/drawingml/chart"] = "http://schemas.openxmlformats.org/drawingml/2006/chart",
        ["http://purl.oclc.org/ooxml/drawingml/chartDrawing"] = "http://schemas.openxmlformats.org/drawingml/2006/chartDrawing",
        ["http://purl.oclc.org/ooxml/drawingml/picture"] = "http://schemas.openxmlformats.org/drawingml/2006/picture",
        ["http://purl.oclc.org/ooxml/drawingml/diagram"] = "http://schemas.openxmlformats.org/drawingml/2006/diagram",
        ["http://purl.oclc.org/ooxml/drawingml/lockedCanvas"] = "http://schemas.openxmlformats.org/drawingml/2006/lockedCanvas",
        ["http://purl.oclc.org/ooxml/officeDocument/sharedTypes"] = "http://schemas.openxmlformats.org/officeDocument/2006/sharedTypes",
        ["http://purl.oclc.org/ooxml/officeDocument/math"] = "http://schemas.openxmlformats.org/officeDocument/2006/math",
    };

    /// <summary>The relationship-id attribute (r:id, r:embed, ...) in either namespace, or null.</summary>
    internal static string? GetRelAttribute(XmlReader x, string localName = "id")
        => x.GetAttribute(localName, RelationshipsNs) ?? x.GetAttribute(localName, RelationshipsStrictNs);

    /// <summary>True if the namespace is a Strict (purl.oclc.org) one this class maps.</summary>
    internal static bool IsStrict(string ns) => Map.ContainsKey(ns);

    /// <summary>Renames Strict-namespace elements and attributes in place to their Transitional names.</summary>
    internal static void Normalize(XElement? root)
    {
        if (root is null) return;
        bool any = false;
        foreach (var e in root.DescendantsAndSelf())
        {
            if (Map.ContainsKey(e.Name.NamespaceName) || e.Attributes().Any(a => a.IsNamespaceDeclaration && Map.ContainsKey(a.Value)))
            { any = true; break; }
        }
        if (!any) return;

        foreach (var e in root.DescendantsAndSelf().ToList())
        {
            if (Map.TryGetValue(e.Name.NamespaceName, out var t)) e.Name = XName.Get(e.Name.LocalName, t);
            if (!e.HasAttributes) continue;
            List<XAttribute>? replaced = null;
            foreach (var a in e.Attributes())
            {
                if (a.IsNamespaceDeclaration)
                {
                    if (Map.TryGetValue(a.Value, out var nt)) a.Value = nt;
                    continue;
                }
                if (Map.TryGetValue(a.Name.NamespaceName, out var at))
                {
                    replaced ??= new List<XAttribute>();
                    replaced.Add(a);
                }
                else if (a.Name.LocalName == "uri" && a.Name.Namespace == XNamespace.None && Map.TryGetValue(a.Value, out var ut))
                {
                    a.Value = ut;   // <a:graphicData uri="..."> names the content's namespace
                }
            }
            if (replaced is null) continue;
            foreach (var a in replaced)
            {
                var name = XName.Get(a.Name.LocalName, Map[a.Name.NamespaceName]);
                a.Remove();
                e.SetAttributeValue(name, a.Value);
            }
        }
    }

    /// <summary>Normalises a loaded document's root; see <see cref="Normalize(XElement?)"/>.</summary>
    internal static void Normalize(XDocument doc) => Normalize(doc.Root);
}

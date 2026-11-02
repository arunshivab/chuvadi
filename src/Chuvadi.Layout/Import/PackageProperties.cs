using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Xml;

namespace Chuvadi.Layout.Import
{
    /// <summary>
    /// Reads the page count and title an Office application recorded in an unencrypted package
    /// (docProps/app.xml Pages, docProps/core.xml dc:title). Used only as the initial page-count estimate and the
    /// document title; any problem gives null.
    /// </summary>
    internal static class PackageProperties
    {
        private const long MaxPartBytes = 1 << 20;

        public static (int? Pages, string? Title) Read(byte[] package)
        {
            if (package.Length < 4 || package[0] != 0x50 || package[1] != 0x4B)
            {
                return (null, null);
            }
            try
            {
                using ZipArchive zip = new ZipArchive(new MemoryStream(package, writable: false), ZipArchiveMode.Read);
                int? pages = null;
                string? title = null;
                if (Text(zip, "docProps/app.xml", "Pages") is string p && int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n > 0)
                {
                    pages = n;
                }
                if (Text(zip, "docProps/core.xml", "title") is string t && t.Trim().Length > 0)
                {
                    title = t.Trim();
                }
                return (pages, title);
            }
            catch (Exception ex) when (ex is InvalidDataException or XmlException or IOException or NotSupportedException)
            {
                return (null, null);
            }
        }

        private static string? Text(ZipArchive zip, string part, string localName)
        {
            ZipArchiveEntry? e = zip.GetEntry(part);
            if (e is null || e.Length > MaxPartBytes)
            {
                return null;
            }
            XmlReaderSettings settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxPartBytes };
            using Stream s = e.Open();
            using XmlReader r = XmlReader.Create(s, settings);
            while (r.Read())
            {
                if (r.NodeType == XmlNodeType.Element && r.LocalName == localName)
                {
                    return r.ReadElementContentAsString();
                }
            }
            return null;
        }
    }
}

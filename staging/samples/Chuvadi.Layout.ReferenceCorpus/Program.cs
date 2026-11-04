using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Docs.Model;
using Chuvadi.Sheets.Excel;

namespace Chuvadi.Layout.ReferenceCorpus
{
    /// <summary>
    /// Usage: Chuvadi.Layout.ReferenceCorpus &lt;output folder&gt;
    /// Writes the 48 docx and 25 xlsx reference documents and cases.tsv (file, family, what it exercises).
    /// The reference PDFs are made next by make-references.sh (LibreOffice headless).
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.Error.WriteLine("Usage: Chuvadi.Layout.ReferenceCorpus <output folder>");
                return 2;
            }
            string folder = args[0];
            Directory.CreateDirectory(folder);
            StringBuilder tsv = new StringBuilder("file\tfamily\texercises\n");
            int count = 0;
            foreach (Case c in DocxCases.All())
            {
                string file = c.Name + ".docx";
                ((Document)c.Build()).SaveTo(Path.Combine(folder, file));
                tsv.Append(file).Append('\t').Append(c.Family).Append('\t').Append(c.Description).Append('\n');
                count++;
            }
            foreach (Case c in XlsxCases.All())
            {
                string file = c.Name + ".xlsx";
                ((Workbook)c.Build()).SaveTo(Path.Combine(folder, file));
                tsv.Append(file).Append('\t').Append(c.Family).Append('\t').Append(c.Description).Append('\n');
                count++;
            }
            File.WriteAllText(Path.Combine(folder, "cases.tsv"), tsv.ToString(), new UTF8Encoding(false));
            Console.WriteLine($"{count} documents written to {Path.GetFullPath(folder)}");
            return 0;
        }
    }
}

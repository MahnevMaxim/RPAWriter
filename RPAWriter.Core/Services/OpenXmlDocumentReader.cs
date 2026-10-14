using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Чтение ТЗ из Word (.docx) и Excel (.xlsx) через Open XML SDK,
    /// а также legacy-форматов (.doc/.xls) через NPOI.
    /// </summary>
    public class OpenXmlDocumentReader : IDocumentReader
    {
        public SpecModel Read(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            if (!File.Exists(path)) throw new FileNotFoundException("Файл не найден", path);

            var ext = Path.GetExtension(path).ToLowerInvariant();
            string text;
            switch (ext)
            {
                case ".docx":
                    text = ReadWordText(path);
                    break;
                case ".xlsx":
                    text = ReadExcelText(path);
                    break;
                case ".doc":
                case ".xls":
                    return new NpoiDocumentReader().Read(path);
                case ".txt":
                case ".md":
                case ".markdown":
                    text = File.ReadAllText(path, Encoding.UTF8);
                    break;
                default:
                    throw new NotSupportedException("Неподдерживаемый формат документа: " + ext);
            }

            return new SpecModel
            {
                Name = Path.GetFileNameWithoutExtension(path),
                SourcePath = path,
                RawText = text
            };
        }

        public string ReadWordText(string path)
        {
            var sb = new StringBuilder();
            using (var doc = WordprocessingDocument.Open(path, false))
            {
                var body = doc.MainDocumentPart?.Document?.Body;
                if (body == null) return "";
                foreach (var element in body.ChildElements)
                {
                    if (element is Paragraph p)
                        sb.AppendLine(ReadParagraph(p));
                    else if (element is DocumentFormat.OpenXml.Wordprocessing.Table table)
                        ReadTable(table, sb);
                }
            }
            return sb.ToString();
        }

        public string ReadExcelText(string path)
        {
            var sb = new StringBuilder();
            using (var doc = SpreadsheetDocument.Open(path, false))
            {
                var workbookPart = doc.WorkbookPart;
                if (workbookPart?.Workbook?.Sheets == null) return "";

                var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable?
                    .Descendants<SharedStringItem>().Select(s => s.InnerText).ToList()
                    ?? new List<string>();

                foreach (var sheet in workbookPart.Workbook.Sheets.ChildElements.OfType<Sheet>())
                {
                    var id = sheet.Id?.Value;
                    if (id == null) continue;
                    var worksheetPart = (WorksheetPart)workbookPart.GetPartById(id);
                    if (worksheetPart?.Worksheet == null) continue;

                    sb.AppendLine("== " + sheet.Name + " ==");
                    foreach (var row in worksheetPart.Worksheet.Descendants<Row>())
                    {
                        var cells = row.Descendants<Cell>().Select(c => ReadCell(c, sharedStrings)).ToList();
                        sb.AppendLine(string.Join("\t", cells));
                    }
                }
            }
            return sb.ToString();
        }

        private static string ReadParagraph(Paragraph p)
        {
            return string.Join("", p.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>().Select(t => t.Text ?? ""));
        }

        private static void ReadTable(DocumentFormat.OpenXml.Wordprocessing.Table table, StringBuilder sb)
        {
            foreach (var row in table.Descendants<TableRow>())
            {
                var cells = row.Descendants<TableCell>()
                    .Select(c => string.Join("", c.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>().Select(t => t.Text ?? "")))
                    .ToList();
                sb.AppendLine(string.Join("\t", cells));
            }
        }

        private static string ReadCell(Cell cell, List<string> sharedStrings)
        {
            var value = cell.InnerText ?? "";
            if (cell.DataType != null && cell.DataType.Value == CellValues.SharedString)
            {
                int index;
                if (int.TryParse(value, out index) && index >= 0 && index < sharedStrings.Count)
                    return sharedStrings[index];
            }
            return value;
        }
    }
}

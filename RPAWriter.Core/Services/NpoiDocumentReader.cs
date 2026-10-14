using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NPOI.HSSF.UserModel;
using NPOI.POIFS.FileSystem;
using NPOI.SS.UserModel;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Чтение legacy-документов (<c>.doc</c> / <c>.xls</c>) через NPOI.
    /// <c>.xls</c> читается полностью (HSSF). <c>.doc</c> — в режиме best-effort:
    /// NPOI не поставляет парсер HWPF (Word 97-2003), поэтому текст извлекается
    /// из OLE2-контейнера («WordDocument» stream) по флагу Unicode в FIB.
    /// </summary>
    public class NpoiDocumentReader : IDocumentReader
    {
        private const string WordDocumentStream = "WordDocument";

        public SpecModel Read(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            if (!File.Exists(path)) throw new FileNotFoundException("Файл не найден", path);

            var ext = Path.GetExtension(path).ToLowerInvariant();
            string text;
            switch (ext)
            {
                case ".doc":
                    text = ReadDocText(path);
                    break;
                case ".xls":
                    text = ReadXlsText(path);
                    break;
                default:
                    throw new NotSupportedException("Неподдерживаемый формат документа для NPOI: " + ext);
            }

            return new SpecModel
            {
                Name = Path.GetFileNameWithoutExtension(path),
                SourcePath = path,
                RawText = text
            };
        }

        /// <summary>Извлекает текст из legacy Word-документа (.doc) через OLE2-контейнер NPOI.</summary>
        public string ReadDocText(string path)
        {
            using (var fs = File.OpenRead(path))
            {
                var poifs = new NPOIFSFileSystem(fs);
                try
                {
                    var entry = poifs.Root.GetEntry(WordDocumentStream);
                    if (entry == null || !entry.IsDocumentEntry) return string.Empty;
                    using (var din = new DocumentInputStream((DocumentEntry)entry))
                    {
                        return ExtractDocText(ReadFully(din));
                    }
                }
                finally
                {
                    poifs.Close();
                }
            }
        }

        /// <summary>Извлекает текст из legacy Excel-книги (.xls) в том же виде, что и .xlsx.</summary>
        public string ReadXlsText(string path)
        {
            var sb = new StringBuilder();
            using (var fs = File.OpenRead(path))
            {
                var workbook = new HSSFWorkbook(fs);
                try
                {
                    var formatter = new DataFormatter();
                    for (int i = 0; i < workbook.NumberOfSheets; i++)
                    {
                        var sheet = workbook.GetSheetAt(i);
                        sb.AppendLine("== " + workbook.GetSheetName(i) + " ==");
                        foreach (IRow row in sheet)
                        {
                            var cells = new List<string>();
                            for (int c = 0; c < row.LastCellNum; c++)
                            {
                                var cell = row.GetCell(c);
                                cells.Add(cell == null ? "" : formatter.FormatCellValue(cell));
                            }
                            sb.AppendLine(string.Join("\t", cells));
                        }
                    }
                }
                finally
                {
                    workbook.Close();
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Best-effort извлечение текста из содержимого потока «WordDocument» файла .doc.
        /// Определяет кодировку по флагу fExtChar (Unicode) в FIB и собирает непрерывные
        /// последовательности печатаемых символов.
        /// </summary>
        public static string ExtractDocText(byte[] wordDocumentStream)
        {
            if (wordDocumentStream == null || wordDocumentStream.Length < 0x0C)
                return string.Empty;

            var sb = new StringBuilder();
            var run = new StringBuilder();

            if (IsUnicode(wordDocumentStream))
            {
                for (int i = 0; i + 1 < wordDocumentStream.Length; i += 2)
                {
                    char c = (char)(wordDocumentStream[i] | (wordDocumentStream[i + 1] << 8));
                    if (IsTextChar(c)) run.Append(c); else FlushRun(sb, run);
                }
            }
            else
            {
                // ANSI (.doc): байт → Latin-1 символ (best-effort).
                for (int i = 0; i < wordDocumentStream.Length; i++)
                {
                    char c = (char)wordDocumentStream[i];
                    if (IsTextChar(c)) run.Append(c); else FlushRun(sb, run);
                }
            }

            FlushRun(sb, run);
            return sb.ToString();
        }

        private static bool IsUnicode(byte[] data)
        {
            // FIB: слово flags по смещению 0x0A; бит 12 (0x1000) — fExtChar (Unicode).
            int flags = data[0x0A] | (data[0x0B] << 8);
            return (flags & 0x1000) != 0;
        }

        private static void FlushRun(StringBuilder result, StringBuilder run)
        {
            if (run.Length >= 4)
            {
                if (result.Length > 0) result.Append('\n');
                result.Append(run.ToString());
            }
            run.Clear();
        }

        private static bool IsTextChar(char c)
        {
            if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)) return true;
            switch (c)
            {
                case '.': case ',': case ';': case ':': case '!': case '?':
                case '-': case '_': case '(': case ')': case '[': case ']':
                case '{': case '}': case '/': case '\\': case '"': case '\'':
                case '+': case '=': case '<': case '>': case '@': case '#':
                case '$': case '%': case '&': case '*': case '~': case '^':
                case '|': case '`':
                    return true;
                default:
                    return false;
            }
        }

        private static byte[] ReadFully(Stream stream)
        {
            using (var ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }

    }
}

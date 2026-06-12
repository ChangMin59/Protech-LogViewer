using DbViewer.Models;
using System.IO;
using System.Text;

namespace DbViewer.Services.Export
{
    public static class TextFileExporter
    {
        private const int DateTimeWidth = 24;
        private const int TypeWidth = 16;
        private const int ActionWidth = 16;
        private const int SectionWidth = 52;
        private const int ContentsWidthBeforePacket = 60;

        public static string Export(
            Func<IEnumerable<LogRow>> sourceRowsFactory,
            string outputPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");

            UTF8Encoding encoding = new(
                encoderShouldEmitUTF8Identifier: true,
                throwOnInvalidBytes: false
            );

            using StreamWriter writer = new(outputPath, append: false, encoding);

            foreach (LogRow row in sourceRowsFactory())
            {
                writer.WriteLine(FormatRow(row));
                writer.WriteLine();
            }

            return outputPath;
        }

        private static string FormatRow(LogRow row)
        {
            string line =
                PadDisplay(row.DTime, DateTimeWidth) +
                PadDisplay(row.Type, TypeWidth) +
                PadDisplay(row.Action, ActionWidth) +
                PadDisplay(row.Section, SectionWidth);

            string packet = row.Packet.Trim();

            if (string.IsNullOrWhiteSpace(packet))
            {
                return (line + row.Contents).TrimEnd();
            }

            return (
                line +
                PadDisplay(row.Contents, ContentsWidthBeforePacket) +
                "     " +
                packet
            ).TrimEnd();
        }

        private static string PadDisplay(string? value, int width)
        {
            string text = value ?? "";
            int displayWidth = GetDisplayWidth(text);

            if (displayWidth >= width)
            {
                return text + " ";
            }

            return text + new string(' ', width - displayWidth);
        }

        private static int GetDisplayWidth(string text)
        {
            int width = 0;

            foreach (char ch in text)
            {
                width += IsWideCharacter(ch)
                    ? 2
                    : 1;
            }

            return width;
        }

        private static bool IsWideCharacter(char ch)
        {
            return ch >= 0x1100 &&
                   (ch <= 0x115F ||
                    ch == 0x2329 ||
                    ch == 0x232A ||
                    (ch >= 0x2E80 && ch <= 0xA4CF) ||
                    (ch >= 0xAC00 && ch <= 0xD7A3) ||
                    (ch >= 0xF900 && ch <= 0xFAFF) ||
                    (ch >= 0xFE10 && ch <= 0xFE19) ||
                    (ch >= 0xFE30 && ch <= 0xFE6F) ||
                    (ch >= 0xFF00 && ch <= 0xFF60) ||
                    (ch >= 0xFFE0 && ch <= 0xFFE6));
        }
    }
}

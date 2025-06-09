using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DeobfuscateStackTrace
{
    public class StackTraceDeObfuscator
    {
        public static void Convert(SymbolMappingReader reader, string oldLogFile, string newLogFile, bool removeMethodGeneratedByObfuz)
        {
            var obfuscatedLines = File.ReadAllLines(oldLogFile, Encoding.UTF8);
            var deObfuscatedLines = new List<string>();

            foreach (string line in obfuscatedLines)
            {
                if (!TryConvertLine(line, reader, out var newLine))
                {
                    newLine = line;
                }
                newLine = reader.TryDeobfuscateTypeName(newLine);
                if (!removeMethodGeneratedByObfuz || !newLine.StartsWith("$Obfuz$"))
                {
                    deObfuscatedLines.Add(newLine);
                }
            }
            File.WriteAllLines(newLogFile, deObfuscatedLines, Encoding.UTF8);
        }

        private static bool TryConvertLine(string line, SymbolMappingReader reader, out string deObfuscatedStackTrace)
        {
            return reader.TryDeobfuscateExceptionStackTrace(line, out deObfuscatedStackTrace) || reader.TryDeobfuscateDebugLogStackTrace(line, out deObfuscatedStackTrace);
        }
    }
}

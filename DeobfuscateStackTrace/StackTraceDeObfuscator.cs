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

            bool logLineFound = true;
            foreach (string line in obfuscatedLines)
            {
                if (TryConvertLine(line, reader, ref logLineFound, out var newLine))
                {
                    if (!removeMethodGeneratedByObfuz || !newLine.StartsWith("$Obfuz$"))
                    {
                        deObfuscatedLines.Add(newLine);
                    }
                }
                else if (!removeMethodGeneratedByObfuz || !line.StartsWith("$Obfuz$"))
                {
                    deObfuscatedLines.Add(line);
                }
            }
            File.WriteAllLines(newLogFile, deObfuscatedLines, Encoding.UTF8);
        }

        private static bool TryConvertLine(string line, SymbolMappingReader reader, ref bool logLineFound, out string deObfuscatedStackTrace)
        {
            if (reader.TryDeobfuscateExceptionStackTrace(line, out deObfuscatedStackTrace))
            {
                return true;
            }
            deObfuscatedStackTrace = line;
            //if (string.IsNullOrEmpty(line))
            //{
            //    logLineFound = false;
            //    return false;
            //}
            //if (!logLineFound)
            //{
            //    logLineFound = line.StartsWith("UnityEngine.DebugLogHandler:Internal_Log")
            //        || line.StartsWith("UnityEngine.DebugLogHandler:LogFormat")
            //        || line.StartsWith("UnityEngine.Logger:Log");
            //    return false;
            //}
            return reader.TryDeobfuscateDebugLogStackTrace(line, out deObfuscatedStackTrace);
        }
    }
}

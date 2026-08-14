// Copyright 2025 Code Philosophy
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

﻿using System;
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
                if (!removeMethodGeneratedByObfuz || !IsMethodGeneratedByObfuz(newLine))
                {
                    deObfuscatedLines.Add(newLine);
                }
            }
            File.WriteAllLines(newLogFile, deObfuscatedLines, Encoding.UTF8);
        }

        private static bool IsMethodGeneratedByObfuz(string line)
        {
            string content = line.TrimStart();
            if (content.StartsWith("at ", StringComparison.Ordinal))
            {
                content = content.Substring(3).TrimStart();
            }
            return content.StartsWith("$Obfuz$", StringComparison.Ordinal) || content.Contains("$Obfuz$ProxyCall") || content.Contains("$Obfuz$Dispatch");
        }

        private static bool TryConvertLine(string line, SymbolMappingReader reader, out string deObfuscatedStackTrace)
        {
            return reader.TryDeobfuscateExceptionStackTrace(line, out deObfuscatedStackTrace) || reader.TryDeobfuscateDebugLogStackTrace(line, out deObfuscatedStackTrace);
        }
    }
}

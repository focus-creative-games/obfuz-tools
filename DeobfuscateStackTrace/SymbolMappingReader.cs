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


using System.Text.RegularExpressions;
using System.Xml;

namespace DeobfuscateStackTrace
{

    public class SymbolMappingReader
    {
        private class MethodSignatureMapping
        {
            public string newMethodParameters;
            public string oldMethodNameWithDeclaringType;
            public string oldMethodParameters;
        }

        private class MethodSignature
        {
            public string newMethodNameWithDeclaringType;
            public List<MethodSignatureMapping> mappings = new List<MethodSignatureMapping>();
        }

        private readonly Dictionary<string, MethodSignature> _methodSignaturesMapping = new Dictionary<string, MethodSignature>();

        private readonly Dictionary<string, string> _typeNameMappings = new Dictionary<string, string>();

        public SymbolMappingReader(string mappingFile)
        {
            LoadXmlMappingFile(mappingFile);
        }

        private void LoadXmlMappingFile(string mappingFile)
        {
            var doc = new XmlDocument();
            doc.Load(mappingFile);
            var root = doc.DocumentElement;
            foreach (XmlNode node in root.ChildNodes)
            {
                if (!(node is XmlElement element))
                {
                    continue;
                }
                LoadAssemblyMapping(element);
            }
        }

        private void LoadAssemblyMapping(XmlElement ele)
        {
            if (ele.Name != "assembly")
            {
                throw new System.Exception($"Invalid node name: {ele.Name}. Expected 'assembly'.");
            }
            foreach (XmlNode node in ele.ChildNodes)
            {
                if (!(node is XmlElement element))
                {
                    continue;
                }
                if (element.Name == "type")
                {
                    LoadTypeMapping(element);
                }
            }
        }

        private void LoadTypeMapping(XmlElement ele)
        {
            if (!ele.HasAttribute("fullName"))
            {
                throw new System.Exception($"Invalid node name: {ele.Name}. attribute 'fullName' missing.");
            }
            if (!ele.HasAttribute("newFullName"))
            {
                throw new System.Exception($"Invalid node name: {ele.Name}. attribute 'newFullName' missing.");
            }
            string oldFullName = ele.Attributes["fullName"].Value;
            string newFullName = ele.Attributes["newFullName"].Value;
            _typeNameMappings[newFullName] = oldFullName;
            if (newFullName.Contains('/'))
            {
                // Stack traces usually show nested types as '+' while mapping may use '/'.
                _typeNameMappings[newFullName.Replace('/', '+')] = oldFullName.Replace('/', '+');
            }
            foreach (XmlNode node in ele.ChildNodes)
            {
                if (!(node is XmlElement c))
                {
                    continue;
                }
                if (node.Name == "method")
                {
                    LoadMethodMapping(c);
                }
            }
        }

        private (string, string) SplitMethodSignature(string signature)
        {
            int index = signature.IndexOf('(');
            if (index < 0)
            {
                return (signature, string.Empty);
            }
            string methodNameWithDeclaringType = signature.Substring(0, index);
            string methodParameters = signature.Substring(index);
            return (methodNameWithDeclaringType, methodParameters);
        }

        private void LoadMethodMapping(XmlElement ele)
        {
            if (!ele.HasAttribute("oldStackTraceSignature"))
            {
                throw new System.Exception($"Invalid node name: {ele.Name}. attribute 'oldStackTraceSignature' missing.");
            }
            if (!ele.HasAttribute("newStackTraceSignature"))
            {
                throw new System.Exception($"Invalid node name: {ele.Name}. attribute 'newStackTraceSignature' missing.");
            }
            string oldStackTraceSignature = ele.Attributes["oldStackTraceSignature"].Value;
            string newStackTraceSignature = ele.Attributes["newStackTraceSignature"].Value;


            (string oldMethodNameWithDeclaringType, string oldMethodParameters) = SplitMethodSignature(oldStackTraceSignature);
            (string newMethodNameWithDeclaringType, string newMethodParameters) = SplitMethodSignature(newStackTraceSignature);

            if (!_methodSignaturesMapping.TryGetValue(newMethodNameWithDeclaringType, out var methodSignature))
            {
                methodSignature = new MethodSignature { newMethodNameWithDeclaringType = newMethodNameWithDeclaringType, };
                _methodSignaturesMapping[newMethodNameWithDeclaringType] = methodSignature;
            }
            methodSignature.mappings.Add(new MethodSignatureMapping
            {
                newMethodParameters = newMethodParameters,
                oldMethodNameWithDeclaringType = oldMethodNameWithDeclaringType,
                oldMethodParameters = oldMethodParameters,
            });
        }

        private static readonly Regex _stackTraceParameterTypeRegex = new Regex(@"(?<![\w.])(?:[A-Za-z_]\w*\.)*([A-Za-z_]\w*)(?:&)?(?=\s+[A-Za-z_]\w*|\s*[,\)])", RegexOptions.Compiled);

        private string NormalizeExceptionParameters(string parameterList)
        {
            // Unity stack trace often prints "Namespace.Type argName" and assembly prefixes.
            // Mapping XML stores canonical forms like "(Object, Int32)".
            return _stackTraceParameterTypeRegex.Replace(parameterList, m => m.Groups[1].Value);
        }


        private (string, string) SplitMethodNameWithDeclaringTypeName(string name)
        {
            int lastColonIndex = name.LastIndexOf(':');
            if (lastColonIndex != -1)
            {
                string declaringTypeName = name.Substring(0, lastColonIndex);
                string methodName = name.Substring(lastColonIndex + 1);
                return (declaringTypeName, methodName);
            }
            return (string.Empty, name); // Return empty declaring type if no colon is found
        }

        private string ConvertToNormalMethodNameWithDeclaringType(string methodName)
        {
            // for .ctor or .cctor
            int lastColonIndex = methodName.LastIndexOf("..");
            if (lastColonIndex == -1)
            {
                lastColonIndex = methodName.LastIndexOf('.');
            }
            if (lastColonIndex != -1)
            {
                return methodName.Substring(0, lastColonIndex) + ":" + methodName.Substring(lastColonIndex + 1);
            }
            return methodName; // Return the original method name if no colon is found
        }

        private string ConvertToExceptionMethodNameWithDeclaringType(string methodName)
        {
            int lastColonIndex = methodName.LastIndexOf(':');
            if (lastColonIndex != -1)
            {
                return methodName.Substring(0, lastColonIndex) + "." + methodName.Substring(lastColonIndex + 1);
            }
            return methodName; // Return the original method name if no colon is found
        }

        private bool TryParseExceptionStackTraceLine(string line, out string prefix, out string methodId, out string parameters, out string tail)
        {
            prefix = string.Empty;
            methodId = string.Empty;
            parameters = string.Empty;
            tail = string.Empty;

            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            int methodStartIndex = 0;
            while (methodStartIndex < line.Length && char.IsWhiteSpace(line[methodStartIndex]))
            {
                methodStartIndex++;
            }

            if (methodStartIndex + 2 < line.Length &&
                line[methodStartIndex] == 'a' &&
                line[methodStartIndex + 1] == 't' &&
                char.IsWhiteSpace(line[methodStartIndex + 2]))
            {
                methodStartIndex += 2;
                while (methodStartIndex < line.Length && char.IsWhiteSpace(line[methodStartIndex]))
                {
                    methodStartIndex++;
                }
            }

            if (methodStartIndex >= line.Length)
            {
                return false;
            }

            int parameterStartIndex = line.IndexOf('(', methodStartIndex);
            if (parameterStartIndex < 0)
            {
                return false;
            }

            int methodEndIndex = parameterStartIndex;
            while (methodEndIndex > methodStartIndex && char.IsWhiteSpace(line[methodEndIndex - 1]))
            {
                methodEndIndex--;
            }

            if (methodEndIndex <= methodStartIndex)
            {
                return false;
            }

            string candidateMethodId = line.Substring(methodStartIndex, methodEndIndex - methodStartIndex);
            for (int i = 0; i < candidateMethodId.Length; i++)
            {
                if (char.IsWhiteSpace(candidateMethodId[i]))
                {
                    return false;
                }
            }

            if (!TrySplitMethodId(candidateMethodId, out _, out _, out _))
            {
                return false;
            }

            int parameterEndIndex = line.IndexOf(')', parameterStartIndex);
            if (parameterEndIndex == -1)
            {
                return false;
            }

            prefix = line.Substring(0, methodStartIndex);
            methodId = candidateMethodId;
            parameters = line.Substring(parameterStartIndex, parameterEndIndex - parameterStartIndex + 1);
            tail = line.Substring(parameterEndIndex + 1);
            return true;
        }

        private bool TrySplitMethodId(string methodId, out string owner, out string methodName, out char separator)
        {
            owner = string.Empty;
            methodName = string.Empty;
            separator = '\0';

            int colonIndex = methodId.LastIndexOf(':');
            int dotIndex = methodId.LastIndexOf('.');
            int separatorIndex;
            if (colonIndex > dotIndex)
            {
                separatorIndex = colonIndex;
                separator = ':';
            }
            else
            {
                separatorIndex = dotIndex;
                separator = '.';
            }

            if (separatorIndex <= 0 || separatorIndex >= methodId.Length - 1)
            {
                return false;
            }

            owner = methodId.Substring(0, separatorIndex);
            methodName = methodId.Substring(separatorIndex + 1);
            return true;
        }

        private bool TryReplaceExceptionStackTraceLine(string line, out string replacedLine)
        {
            replacedLine = line;
            if (!TryParseExceptionStackTraceLine(line, out var prefix, out var methodId, out var obfuscatedMethodParameters, out var tail))
            {
                return false;
            }

            if (!TrySplitMethodId(methodId, out var obfuscatedDeclaringTypeName, out var obfuscatedMethodName, out var separator))
            {
                return false;
            }
            string obfuscatedExceptionMethodNameWithDeclaringType = $"{obfuscatedDeclaringTypeName}:{obfuscatedMethodName}";
            string normalizedObfuscatedMethodParameters = NormalizeExceptionParameters(obfuscatedMethodParameters);
            if (_methodSignaturesMapping.TryGetValue(obfuscatedExceptionMethodNameWithDeclaringType, out var methodSignature))
            {
                foreach (var mapping in methodSignature.mappings)
                {
                    if (mapping.newMethodParameters == obfuscatedMethodParameters ||
                        mapping.newMethodParameters == normalizedObfuscatedMethodParameters)
                    {
                        (string oldDeclaringTypeName, string oldMethodName) = SplitMethodNameWithDeclaringTypeName(mapping.oldMethodNameWithDeclaringType);
                        replacedLine = $"{prefix}{oldDeclaringTypeName}{separator}{oldMethodName}{mapping.oldMethodParameters}{tail}";
                        return true;
                    }
                }
                {
                    MethodSignatureMapping mapping = methodSignature.mappings[0];
                    (string oldDeclaringTypeName, string oldMethodName) = SplitMethodNameWithDeclaringTypeName(mapping.oldMethodNameWithDeclaringType);
                    replacedLine = $"{prefix}{oldDeclaringTypeName}{separator}{oldMethodName}{obfuscatedMethodParameters}{tail}";
                    return true;
                }
            }
            return false;
        }

        public bool TryDeobfuscateExceptionStackTrace(string obfuscatedStackTraceLog, out string oldFullSignature)
        {
            if (TryReplaceExceptionStackTraceLine(obfuscatedStackTraceLog, out oldFullSignature))
            {
                return true;
            }
            oldFullSignature = obfuscatedStackTraceLog;
            return false;
        }

        private Regex _normalStackTraceRegex = new Regex(@"^(\S+):(\S+)(\([^)]*\))$", RegexOptions.Compiled);

        private string ReplaceNormalStackTraceMatch(Match m)
        {
            string obfuscatedDeclaringTypeName = m.Groups[1].Value;
            string obfuscatedMethodName = m.Groups[2].Value;
            string obfuscatedMethodNameWithDeclaringType = $"{obfuscatedDeclaringTypeName}:{obfuscatedMethodName}";
            string obfuscatedMethodParameters = m.Groups[3].Value;
            if (_methodSignaturesMapping.TryGetValue(obfuscatedMethodNameWithDeclaringType, out var methodSignature))
            {
                foreach (var mapping in methodSignature.mappings)
                {
                    if (mapping.newMethodParameters == obfuscatedMethodParameters)
                    {
                        return $"{mapping.oldMethodNameWithDeclaringType}{mapping.oldMethodParameters}";
                    }
                }
                MethodSignatureMapping matchMapping = methodSignature.mappings[0];
                return $"{matchMapping.oldMethodNameWithDeclaringType}{obfuscatedMethodParameters}";
            }
            return m.Value; // Return the original match if no mapping is found
        }

        public bool TryDeobfuscateDebugLogStackTrace(string obfuscatedStackTraceLog, out string oldFullSignature)
        {
            oldFullSignature = _normalStackTraceRegex.Replace(obfuscatedStackTraceLog, ReplaceNormalStackTraceMatch, 1);
            return oldFullSignature != obfuscatedStackTraceLog;
        }

        // Whole obfuscated nested type as in stack traces before outer is resolved, e.g. $qA.$OLA+$sLA
        private readonly Regex _obfuscatedQualifiedNestedTypeRegex = new Regex(
            @"\$[$a-zA-Z_][\w$]*(?:\.\$[$a-zA-Z_][\w$]*)*(?:[+/]\$[$a-zA-Z_][\w$]*)+",
            RegexOptions.Compiled);

        private readonly Regex _typeNameRegex = new Regex(@"\$[$a-zA-Z_]+([./]\$[$a-zA-Z_]+)*", RegexOptions.Compiled);

        // Outer type may include generic arity (e.g. TaskForm`1+$oV[[T,...]]) — \w does not match backtick.
        private readonly Regex _qualifiedNestedObfuscatedTypeRegex = new Regex(
            @"[A-Za-z_]\w*(?:`+\d+)?(?:\.[A-Za-z_]\w*(?:`+\d+)?)*(?:[+/]\$[$a-zA-Z_][\w$]*)+",
            RegexOptions.Compiled);

        private string ReplaceTypeNameMatch(Match m)
        {
            string obfuscatedTypeName = m.Value;
            if (_typeNameMappings.TryGetValue(obfuscatedTypeName, out var originalTypeName))
            {
                return originalTypeName;
            }
            return obfuscatedTypeName; // Return the original type name if no mapping is found
        }

        public string TryDeobfuscateTypeName(string obfuscatedStackTraceLog)
        {
            // Resolve full obfuscated nested names first so partial outer replace does not break +nested lookup.
            string withObfuscatedNestedReplaced = _obfuscatedQualifiedNestedTypeRegex.Replace(obfuscatedStackTraceLog, ReplaceTypeNameMatch);
            string withSimpleTypeReplaced = _typeNameRegex.Replace(withObfuscatedNestedReplaced, ReplaceTypeNameMatch);
            return _qualifiedNestedObfuscatedTypeRegex.Replace(withSimpleTypeReplaced, ReplaceTypeNameMatch);
        }
    }
}

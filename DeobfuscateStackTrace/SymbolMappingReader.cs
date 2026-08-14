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

        // Nested types are recorded as Outer/Inner in mapping, but IL2CPP exception stacks use Outer+Inner.
        // Simple nested names like `$a` are also used in Debug.Log stacks (`$a:MoveNext()`).
        private readonly Dictionary<string, string> _uniqueNestedSimpleTypeNames = new Dictionary<string, string>();
        private readonly HashSet<string> _ambiguousNestedSimpleTypeNames = new HashSet<string>();

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
            RegisterTypeNameMapping(newFullName, oldFullName);
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

        private void RegisterTypeNameMapping(string newFullName, string oldFullName)
        {
            if (string.IsNullOrEmpty(newFullName))
            {
                return;
            }
            _typeNameMappings[newFullName] = oldFullName;
            if (newFullName.Contains("/"))
            {
                _typeNameMappings[newFullName.Replace('/', '+')] = oldFullName.Replace('/', '+');
                string simpleName = newFullName.Substring(newFullName.LastIndexOf('/') + 1);
                RegisterNestedSimpleTypeName(simpleName, oldFullName);
            }
        }

        private void RegisterNestedSimpleTypeName(string simpleName, string oldFullName)
        {
            if (_ambiguousNestedSimpleTypeNames.Contains(simpleName))
            {
                return;
            }
            if (_uniqueNestedSimpleTypeNames.TryGetValue(simpleName, out var existing) && existing != oldFullName)
            {
                _uniqueNestedSimpleTypeNames.Remove(simpleName);
                _ambiguousNestedSimpleTypeNames.Add(simpleName);
                return;
            }
            _uniqueNestedSimpleTypeNames[simpleName] = oldFullName;
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
            return (string.Empty, name);
        }

        private MethodSignatureMapping FindMethodMapping(MethodSignature methodSignature, string obfuscatedMethodParameters)
        {
            string normalizedObfuscatedParameters = NormalizeParameters(obfuscatedMethodParameters);
            foreach (var mapping in methodSignature.mappings)
            {
                if (mapping.newMethodParameters == obfuscatedMethodParameters
                    || NormalizeParameters(mapping.newMethodParameters) == normalizedObfuscatedParameters)
                {
                    return mapping;
                }
            }
            if (normalizedObfuscatedParameters == "()")
            {
                MethodSignatureMapping emptyParamMapping = methodSignature.mappings.Find(m => NormalizeParameters(m.newMethodParameters) == "()");
                if (emptyParamMapping != null)
                {
                    return emptyParamMapping;
                }
            }
            int obfuscatedParamCount = CountParameters(normalizedObfuscatedParameters);
            MethodSignatureMapping sameCountMapping = methodSignature.mappings.Find(m => CountParameters(NormalizeParameters(m.newMethodParameters)) == obfuscatedParamCount);
            if (sameCountMapping != null)
            {
                return sameCountMapping;
            }
            return methodSignature.mappings[0];
        }

        private static int CountParameters(string parameters)
        {
            if (string.IsNullOrEmpty(parameters) || parameters == "()")
            {
                return 0;
            }
            int count = 1;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i] == ',')
                {
                    count++;
                }
            }
            return count;
        }

        private static string NormalizeParameters(string parameters)
        {
            if (string.IsNullOrEmpty(parameters))
            {
                return "()";
            }
            string inner = parameters.Trim();
            if (inner.StartsWith("(") && inner.EndsWith(")") && inner.Length >= 2)
            {
                inner = inner.Substring(1, inner.Length - 2);
            }
            inner = inner.Trim();
            if (inner.Length == 0)
            {
                return "()";
            }

            var parts = inner.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = NormalizeSingleParameter(parts[i]);
            }
            return "(" + string.Join(", ", parts) + ")";
        }

        private static string NormalizeSingleParameter(string parameter)
        {
            string p = parameter.Trim();
            if (p.Length == 0)
            {
                return p;
            }

            int lastSpace = p.LastIndexOf(' ');
            if (lastSpace > 0)
            {
                string maybeName = p.Substring(lastSpace + 1);
                if (maybeName.IndexOfAny(new[] { '.', '[', ']', '`', '*', '&', '<' }) < 0)
                {
                    p = p.Substring(0, lastSpace).Trim();
                }
            }

            int suffixIndex = p.IndexOfAny(new[] { '[', '*', '&' });
            string typePart = suffixIndex >= 0 ? p.Substring(0, suffixIndex) : p;
            string suffix = suffixIndex >= 0 ? p.Substring(suffixIndex) : string.Empty;
            int lastDot = typePart.LastIndexOf('.');
            if (lastDot >= 0 && lastDot < typePart.Length - 1)
            {
                typePart = typePart.Substring(lastDot + 1);
            }
            return typePart + suffix;
        }

        private bool TryGetMethodSignature(string declaringTypeName, string methodName, out MethodSignature methodSignature)
        {
            foreach (string key in EnumerateMethodLookupKeys(declaringTypeName, methodName))
            {
                if (_methodSignaturesMapping.TryGetValue(key, out methodSignature))
                {
                    return true;
                }
            }
            methodSignature = null;
            return false;
        }

        private static IEnumerable<string> EnumerateMethodLookupKeys(string declaringTypeName, string methodName)
        {
            yield return $"{declaringTypeName}:{methodName}";
            if (declaringTypeName.Contains('+'))
            {
                yield return $"{declaringTypeName.Replace('+', '/')}:{methodName}";
            }
            if (declaringTypeName.Contains('/'))
            {
                yield return $"{declaringTypeName.Replace('/', '+')}:{methodName}";
            }

            int nestedSep = Math.Max(declaringTypeName.LastIndexOf('/'), declaringTypeName.LastIndexOf('+'));
            if (nestedSep >= 0)
            {
                yield return $"{declaringTypeName.Substring(nestedSep + 1)}:{methodName}";
            }
        }

        private bool TryDeobfuscateDeclaringTypeName(string obfuscatedTypeName, out string originalTypeName)
        {
            if (_typeNameMappings.TryGetValue(obfuscatedTypeName, out originalTypeName))
            {
                return true;
            }
            if (obfuscatedTypeName.Contains('+'))
            {
                if (_typeNameMappings.TryGetValue(obfuscatedTypeName.Replace('+', '/'), out originalTypeName))
                {
                    return true;
                }
            }
            if (_uniqueNestedSimpleTypeNames.TryGetValue(obfuscatedTypeName, out originalTypeName))
            {
                return true;
            }
            originalTypeName = obfuscatedTypeName;
            return false;
        }

        private static bool TrySplitTypeAndMethod(string typeAndMethod, out string declaringTypeName, out string methodName)
        {
            declaringTypeName = null;
            methodName = null;
            if (string.IsNullOrEmpty(typeAndMethod))
            {
                return false;
            }

            if (typeAndMethod.EndsWith("..ctor", StringComparison.Ordinal))
            {
                declaringTypeName = typeAndMethod.Substring(0, typeAndMethod.Length - "..ctor".Length);
                methodName = ".ctor";
                return declaringTypeName.Length > 0;
            }
            if (typeAndMethod.EndsWith("..cctor", StringComparison.Ordinal))
            {
                declaringTypeName = typeAndMethod.Substring(0, typeAndMethod.Length - "..cctor".Length);
                methodName = ".cctor";
                return declaringTypeName.Length > 0;
            }

            int lastDot = typeAndMethod.LastIndexOf('.');
            if (lastDot <= 0 || lastDot >= typeAndMethod.Length - 1)
            {
                return false;
            }
            declaringTypeName = typeAndMethod.Substring(0, lastDot);
            methodName = typeAndMethod.Substring(lastDot + 1);
            return true;
        }

        private static bool TryParseParentheses(string text, int startIndex, out int parenStart, out int parenEnd)
        {
            parenStart = text.IndexOf('(', startIndex);
            parenEnd = -1;
            if (parenStart < 0)
            {
                return false;
            }
            parenEnd = text.IndexOf(')', parenStart + 1);
            return parenEnd > parenStart;
        }

        private string DeobfuscateExceptionFrame(string leading, string declaringTypeName, string methodName, string parameters, string suffix)
        {
            if (TryGetMethodSignature(declaringTypeName, methodName, out var methodSignature))
            {
                MethodSignatureMapping mapping = FindMethodMapping(methodSignature, parameters);
                (string oldDeclaringTypeName, string oldMethodName) = SplitMethodNameWithDeclaringTypeName(mapping.oldMethodNameWithDeclaringType);
                if (declaringTypeName.Contains('+'))
                {
                    oldDeclaringTypeName = oldDeclaringTypeName.Replace('/', '+');
                }
                return $"{leading}{oldDeclaringTypeName}.{oldMethodName}{mapping.oldMethodParameters}{suffix}";
            }
            if (TryDeobfuscateDeclaringTypeName(declaringTypeName, out var originalTypeName) && originalTypeName != declaringTypeName)
            {
                if (declaringTypeName.Contains('+'))
                {
                    originalTypeName = originalTypeName.Replace('/', '+');
                }
                return $"{leading}{originalTypeName}.{methodName}{parameters}{suffix}";
            }
            return null;
        }

        private string DeobfuscateDebugFrame(string leading, string declaringTypeName, string methodName, string parameters, string trailing)
        {
            if (TryGetMethodSignature(declaringTypeName, methodName, out var methodSignature))
            {
                MethodSignatureMapping mapping = FindMethodMapping(methodSignature, parameters);
                return $"{leading}{mapping.oldMethodNameWithDeclaringType}{mapping.oldMethodParameters}{trailing}";
            }
            if (TryDeobfuscateDeclaringTypeName(declaringTypeName, out var originalTypeName) && originalTypeName != declaringTypeName)
            {
                return $"{leading}{originalTypeName}:{methodName}{parameters}{trailing}";
            }
            return null;
        }

        public bool TryDeobfuscateExceptionStackTrace(string obfuscatedStackTraceLog, out string oldFullSignature)
        {
            oldFullSignature = obfuscatedStackTraceLog;
            if (string.IsNullOrEmpty(obfuscatedStackTraceLog))
            {
                return false;
            }

            int atIndex = obfuscatedStackTraceLog.IndexOf("at ", StringComparison.Ordinal);
            if (atIndex < 0)
            {
                return false;
            }
            for (int i = 0; i < atIndex; i++)
            {
                if (!char.IsWhiteSpace(obfuscatedStackTraceLog[i]))
                {
                    return false;
                }
            }

            string rest = obfuscatedStackTraceLog.Substring(atIndex + 3);
            if (!TryParseParentheses(rest, 0, out int parenStart, out int parenEnd))
            {
                return false;
            }

            string typeAndMethod = rest.Substring(0, parenStart).TrimEnd();
            if (!TrySplitTypeAndMethod(typeAndMethod, out string declaringTypeName, out string methodName))
            {
                return false;
            }

            string parameters = rest.Substring(parenStart, parenEnd - parenStart + 1);
            string suffix = rest.Substring(parenEnd + 1);
            string leading = obfuscatedStackTraceLog.Substring(0, atIndex + 3);
            string deobfuscated = DeobfuscateExceptionFrame(leading, declaringTypeName, methodName, parameters, suffix);
            if (deobfuscated == null)
            {
                return false;
            }
            oldFullSignature = deobfuscated;
            return oldFullSignature != obfuscatedStackTraceLog;
        }

        public bool TryDeobfuscateDebugLogStackTrace(string obfuscatedStackTraceLog, out string oldFullSignature)
        {
            oldFullSignature = obfuscatedStackTraceLog;
            if (string.IsNullOrEmpty(obfuscatedStackTraceLog))
            {
                return false;
            }

            int contentStart = 0;
            while (contentStart < obfuscatedStackTraceLog.Length && char.IsWhiteSpace(obfuscatedStackTraceLog[contentStart]))
            {
                contentStart++;
            }
            if (contentStart >= obfuscatedStackTraceLog.Length)
            {
                return false;
            }

            string content = obfuscatedStackTraceLog.Substring(contentStart);
            if (content.StartsWith("at ", StringComparison.Ordinal))
            {
                return false;
            }
            if (!TryParseParentheses(content, 0, out int parenStart, out int parenEnd))
            {
                return false;
            }

            string typeAndMethod = content.Substring(0, parenStart);
            int lastColon = typeAndMethod.LastIndexOf(':');
            if (lastColon <= 0 || lastColon >= typeAndMethod.Length - 1)
            {
                return false;
            }
            if (typeAndMethod.IndexOf(' ') >= 0)
            {
                return false;
            }

            string declaringTypeName = typeAndMethod.Substring(0, lastColon);
            string methodName = typeAndMethod.Substring(lastColon + 1);
            string parameters = content.Substring(parenStart, parenEnd - parenStart + 1);
            string trailing = content.Substring(parenEnd + 1);
            string leading = obfuscatedStackTraceLog.Substring(0, contentStart);
            string deobfuscated = DeobfuscateDebugFrame(leading, declaringTypeName, methodName, parameters, trailing);
            if (deobfuscated == null)
            {
                return false;
            }
            oldFullSignature = deobfuscated;
            return oldFullSignature != obfuscatedStackTraceLog;
        }

        // Do not treat method names as types: `$C (` in `test.$C (...)` is a method, not type `$C`.
        private readonly Regex _typeNameRegex = new Regex(@"\$[$a-zA-Z_]+(?:[./+]\$[$a-zA-Z_]+)*(?!\s*\()", RegexOptions.Compiled);

        private string ReplaceTypeNameMatch(Match m)
        {
            string obfuscatedTypeName = m.Value;
            if (TryDeobfuscateDeclaringTypeName(obfuscatedTypeName, out var originalTypeName))
            {
                return originalTypeName;
            }
            return obfuscatedTypeName;
        }

        public string TryDeobfuscateTypeName(string obfuscatedStackTraceLog)
        {
            return _typeNameRegex.Replace(obfuscatedStackTraceLog, ReplaceTypeNameMatch);
        }
    }
}

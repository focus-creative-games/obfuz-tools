
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


        private string GetMethodSignatureWithoutParams(string signature)
        {
            int index = signature.IndexOf('(');
            if (index < 0)
            {
                return signature;
            }
            return signature.Substring(0, index);
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

            if (!_methodSignaturesMapping.TryGetValue(oldMethodNameWithDeclaringType, out var methodSignature))
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

        private Regex _exceptionStackTraceRegex = new Regex(@"^(\s*at\s+)(\S+)\s*(\([^)]*\))(\s+\[\S+]\s+in)", RegexOptions.Compiled);


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

        private string ReplaceExceptionStackTraceMatch(Match m)
        {
            string obfuscatedMethodNameWithDeclaringType = m.Groups[2].Value;
            string obfuscatedExceptionMethodNameWithDeclaringType = ConvertToNormalMethodNameWithDeclaringType(obfuscatedMethodNameWithDeclaringType);
            string obfuscatedMethodParameters = m.Groups[3].Value;
            if (_methodSignaturesMapping.TryGetValue(obfuscatedExceptionMethodNameWithDeclaringType, out var methodSignature))
            {
                foreach (var mapping in methodSignature.mappings)
                {
                    if (mapping.newMethodParameters == obfuscatedMethodParameters)
                    {
                        return $"{m.Groups[1].Value}{ConvertToExceptionMethodNameWithDeclaringType(mapping.oldMethodNameWithDeclaringType)}{mapping.oldMethodParameters}{m.Groups[4].Value}";
                    }
                }
                MethodSignatureMapping matchMapping = methodSignature.mappings[0];
                return $"{m.Groups[1].Value}{ConvertToExceptionMethodNameWithDeclaringType(matchMapping.oldMethodNameWithDeclaringType)}{obfuscatedMethodParameters}{m.Groups[4].Value}";
            }
            return m.Value; // Return the original match if no mapping is found
        }

        private bool TryMatchExceptionStackTrace(string obfuscatedStackTraceLog, out string oldFullSignature)
        {
            oldFullSignature = _exceptionStackTraceRegex.Replace(obfuscatedStackTraceLog, ReplaceExceptionStackTraceMatch, 1);
            return oldFullSignature != obfuscatedStackTraceLog;
        }

        private Regex _normalStackTraceRegex = new Regex(@"^(\S+)(\([^)]*\))", RegexOptions.Compiled);

        private string ReplaceNormalStackTraceMatch(Match m)
        {
            string obfuscatedMethodNameWithDeclaringType = m.Groups[1].Value;
            string obfuscatedMethodParameters = m.Groups[2].Value;
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

        private bool TryMatchNormalStackTrace(string obfuscatedStackTraceLog, out string oldFullSignature)
        {
            oldFullSignature = _normalStackTraceRegex.Replace(obfuscatedStackTraceLog, ReplaceNormalStackTraceMatch, 1);
            return oldFullSignature != obfuscatedStackTraceLog;
        }


        public bool TryDeObfuscateStackTrace(string obfuscatedStackTraceLog, out string deObfuscatedStackTrace)
        {
            return TryMatchExceptionStackTrace(obfuscatedStackTraceLog, out deObfuscatedStackTrace)
                || TryMatchNormalStackTrace(obfuscatedStackTraceLog, out deObfuscatedStackTrace);
        }
    }
}

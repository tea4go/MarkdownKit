using System;
using System.Collections.Generic;
using System.Text;

namespace MarkdownKit.PlantUml
{
    /// <summary>
    /// PlantUML 预处理：处理 !define / !include 等指令，并做标识符替换。
    /// 实测规则：!define NAME value 之后，正文里出现的 NAME（整词）会被换成 value，
    /// 前后的标点原样保留——!FOO 变成 !hello、%FOO% 变成 %hello%、裸 FOO 变成 hello。
    /// !include 要读文件，预览里没有相对文件可读，直接跳过。
    /// </summary>
    internal static class PlantUmlPreprocessor
    {
        public static string Process(string source)
        {
            if (string.IsNullOrEmpty(source) || source.IndexOf('!') < 0)
                return source;

            string[] lines = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var defines = new Dictionary<string, string>();
            var output = new List<string>(lines.Length);

            foreach (string line in lines)
            {
                string trimmed = line.TrimStart();
                if (trimmed.Length > 0 && trimmed[0] == '!')
                {
                    // !NAME 这种是"引用 !define 定义过的常量"，要当正文做替换；
                    // 只有真正的指令（!define / !include / !pragma …）才丢掉
                    string word = FirstWord(trimmed.Substring(1));
                    if (!defines.ContainsKey(word))
                    {
                        Define(trimmed, defines);
                        continue;   // 指令行本身不进正文
                    }
                }
                output.Add(defines.Count == 0 ? line : Substitute(line, defines));
            }
            return string.Join("\n", output);
        }

        /// <summary>取一行里第一个词（到空白为止）。</summary>
        private static string FirstWord(string text)
        {
            int i = 0;
            while (i < text.Length && !char.IsWhiteSpace(text[i]))
                i++;
            return text.Substring(0, i);
        }

        /// <summary>只认 !define NAME value；!include / !pragma 等一律丢掉。</summary>
        private static void Define(string line, Dictionary<string, string> defines)
        {
            const string prefix = "!define ";
            if (!line.StartsWith(prefix, StringComparison.Ordinal))
                return;

            string rest = line.Substring(prefix.Length);
            int i = 0;
            while (i < rest.Length && !char.IsWhiteSpace(rest[i]))
                i++;
            if (i == 0)
                return;

            defines[rest.Substring(0, i)] = rest.Substring(i).Trim();
        }

        /// <summary>整词替换：NAME 前后必须是非标识符字符，免得把 FOO 换进 FOOBAR 里。</summary>
        private static string Substitute(string text, Dictionary<string, string> defines)
        {
            var sb = new StringBuilder(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                if (!IsIdentChar(text[i]))
                {
                    sb.Append(text[i]);
                    i++;
                    continue;
                }

                int start = i;
                while (i < text.Length && IsIdentChar(text[i]))
                    i++;
                string word = text.Substring(start, i - start);
                string value;
                sb.Append(defines.TryGetValue(word, out value) ? value : word);
            }
            return sb.ToString();
        }

        private static bool IsIdentChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_';
        }
    }
}

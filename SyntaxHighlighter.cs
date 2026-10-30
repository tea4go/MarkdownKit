using System;
using System.Collections.Generic;

namespace MarkdownKit
{
    /// <summary>高亮片段的类型。Plain 是普通文本，渲染时用正文色。</summary>
    public enum TokenKind
    {
        Plain,
        Keyword,
        String,
        Comment,
        Number,
        Function,
        Type,

        /// <summary>标记语言的标签名：&lt;div&gt; 里的 div。</summary>
        Tag,

        /// <summary>标签里的属性名：class="x" 里的 class。</summary>
        Attribute,

        /// <summary>属性名/键名：CSS 的 color:、YAML 的 name:、SCSS 的 $primary:。</summary>
        Property,

        /// <summary>段落名：INI/TOML 的 [database]。</summary>
        Section,

        /// <summary>变量名：PowerShell 的 $path、$_。</summary>
        Variable
    }

    /// <summary>一段带类型的高亮片段，渲染器按 Kind 取色。</summary>
    public struct CodeToken
    {
        public readonly string Text;
        public readonly TokenKind Kind;

        public CodeToken(string text, TokenKind kind)
        {
            Text = text;
            Kind = kind;
        }
    }

    /// <summary>语言所属的词法族。用于让调用方按族（而不是具体语言）决定处理方式。</summary>
    public enum SyntaxFamily
    {
        None,
        CStyle,
        Python,
        Hash,
        Sql,
        Json,
        Markup,
        Pascal,
        Basic,
        Css,
        Scss,
        Yaml,
        R,
        Lua,
        Ini,
        PowerShell,
        Dockerfile,
        Makefile
    }

    /// <summary>
    /// 轻量语法高亮器。不做完整的词法分析：先按语言族切出注释和字符串，再在余下的文本里认出
    /// 关键字、内置类型、数字，以及「标识符 + (」形式的函数名，够译文里那些示例代码用。
    /// 认不出的语言一律整段当纯文本返回——宁可不上色，也不要把普通文字错染成关键字。
    /// </summary>
    public static class SyntaxHighlighter
    {
        /// <summary>一个语言族的词法规则。字段为 null 表示该语言没有这种结构。</summary>
        public sealed class Syntax
        {
            public readonly string LineComment;
            public readonly string BlockOpen;
            public readonly string BlockClose;

            /// <summary>引号种类，已按长度降序排好，长引号（三引号）先匹配。</summary>
            public readonly string[] Quotes;

            public readonly HashSet<string> Keywords;
            public readonly HashSet<string> Types;

            /// <summary>
            /// 标记语言（HTML/XML/Vue 等）走另一套扫描器：关键字表给不出标签名和属性名的位置信息，
            /// 只能按 &lt;...&gt; 的结构切。见 HighlightMarkup。
            /// </summary>
            public readonly bool Markup;

            /// <summary>第二种块注释（Delphi 的 { } 之外还有 (* *)），没有就为 null。</summary>
            public readonly string BlockOpen2;
            public readonly string BlockClose2;

            /// <summary>开关不同的成对引号（Lua 的 [[ ]]），元素是 [开, 闭]。</summary>
            public readonly string[][] QuotePairs;

            /// <summary>为真时，「标识符 + 冒号」当成属性名/键名（CSS、SCSS、YAML）。</summary>
            public readonly bool PropertyColon;

            /// <summary>
            /// 为真时行注释必须写在行首或空白之后才生效。YAML 需要：#003366 这种颜色值里的 #
            /// 不是注释；shell/Lua 这些保持「见到就算」的老行为。
            /// </summary>
            public readonly bool LineCommentAfterSpace;

            /// <summary>为真时连字符算标识符的一部分（CSS 的 font-size、YAML 的 data-id）。</summary>
            public readonly bool HyphenInIdent;

            /// <summary>第二种行注释（INI 的 ; 之外还有 #）。</summary>
            public readonly string LineComment2;

            /// <summary>为真时「行首标识符 + 等号」当成键名（INI、TOML 的 key = value）。</summary>
            public readonly bool PropertyEquals;

            /// <summary>为真时行首的 &lt;[name]&gt; 是段落名（INI、TOML）。</summary>
            public readonly bool SectionBrackets;

            /// <summary>为真时 $ 开头的标识符是变量（PowerShell）。</summary>
            public readonly bool DollarVariable;

            /// <summary>为真时带连字符的标识符是 cmdlet（PowerShell 的 Get-ChildItem）。</summary>
            public readonly bool CmdletIdentifier;

            /// <summary>为真时 Makefile 的行首「名字 + 冒号」是构建目标（Section），「名字 + :=/=/+=」是变量。</summary>
            public readonly bool MakeTarget;

            /// <summary>为真时行首到「本行第一个 {」之间是选择器区（CSS/SCSS 的 .btn、#id、div、:hover）。</summary>
            public readonly bool CssSelectors;

            /// <summary>为真时「引号串 + 冒号」是键（JSON 的 "name":），键和值要分开上色。</summary>
            public readonly bool QuotedKey;

            /// <summary>为真时 &amp;anchor / *alias 是变量色（YAML）。</summary>
            public readonly bool YamlAnchors;

            /// <summary>为真时行尾的 | / &gt; 后面那些缩进行是多行标量，按字符串上色（YAML）。</summary>
            public readonly bool BlockScalar;

            public Syntax(string lineComment, string blockOpen, string blockClose,
                string[] quotes, HashSet<string> keywords, HashSet<string> types,
                bool markup = false, string blockOpen2 = null, string blockClose2 = null,
                string[][] quotePairs = null, bool propertyColon = false,
                bool lineCommentAfterSpace = false, bool hyphenInIdent = false,
                string lineComment2 = null, bool propertyEquals = false,
                bool sectionBrackets = false, bool dollarVariable = false,
                bool cmdletIdentifier = false, bool makeTarget = false, bool cssSelectors = false,
                bool quotedKey = false, bool yamlAnchors = false, bool blockScalar = false)
            {
                LineComment = lineComment;
                BlockOpen = blockOpen;
                BlockClose = blockClose;
                Quotes = quotes;
                if (Quotes != null)
                    Array.Sort(Quotes, (a, b) => b.Length.CompareTo(a.Length));
                Keywords = keywords;
                Types = types;
                Markup = markup;
                BlockOpen2 = blockOpen2;
                BlockClose2 = blockClose2;
                QuotePairs = quotePairs;
                PropertyColon = propertyColon;
                LineCommentAfterSpace = lineCommentAfterSpace;
                HyphenInIdent = hyphenInIdent;
                LineComment2 = lineComment2;
                PropertyEquals = propertyEquals;
                SectionBrackets = sectionBrackets;
                DollarVariable = dollarVariable;
                CmdletIdentifier = cmdletIdentifier;
                MakeTarget = makeTarget;
                CssSelectors = cssSelectors;
                QuotedKey = quotedKey;
                YamlAnchors = yamlAnchors;
                BlockScalar = blockScalar;
            }
        }

        private static readonly Syntax CStyle = new Syntax(
            "//", "/*", "*/",
            new[] { "\"", "'", "`" },
            Set("abstract as async await base break case catch checked class const continue "
                + "default defer delete do else enum event explicit export extends extern false "
                + "final finally for foreach func function go goto if implements implicit import in "
                + "instanceof interface is let lock namespace new nil null operator package private "
                + "protected public readonly ref return sealed sizeof static struct super switch "
                + "this throw throws try typedef typeof unchecked using var virtual volatile while yield"),
            Set("bool byte char decimal double dynamic float int long object sbyte short string "
                + "uint ulong ushort void"));

        private static readonly Syntax PythonStyle = new Syntax(
            "#", null, null,
            new[] { "\"\"\"", "'''", "\"", "'" },
            Set("and as assert async await break class continue def del elif else except False "
                + "finally for from global if import in is lambda None nonlocal not or pass raise "
                + "return True try while with yield"),
            null);

        private static readonly Syntax HashStyle = new Syntax(
            "#", null, null,
            new[] { "\"", "'" },
            Set("if then else elif fi for while do done case esac function in return export local "
                + "echo source exit set unset readonly declare true false yes no null"),
            null);

        private static readonly Syntax SqlStyle = new Syntax(
            "--", "/*", "*/",
            new[] { "'", "\"" },
            SetIgnoreCase("select from where insert into values update set delete create table drop "
                + "alter add index view join inner left right outer full on group by order having "
                + "limit offset union all distinct as and or not null is in between like exists case "
                + "when then else end primary key foreign references default unique check constraint "
                + "database if count sum avg min max asc desc"),
            SetIgnoreCase("int integer bigint smallint tinyint varchar nvarchar char text date "
                + "datetime timestamp boolean bool decimal numeric float double real blob"));

        private static readonly Syntax JsonStyle = new Syntax(
            null, null, null,
            new[] { "\"" },
            Set("true false null"),
            null,
            quotedKey: true);

        // 标记语言没有关键字表：标签名和属性名靠 <...> 的结构定位，不用词表。
        private static readonly Syntax MarkupStyle = new Syntax(
            null, "<!--", "-->",
            new[] { "\"", "'" },
            null,
            null,
            true);

        // Delphi / Object Pascal：// 与 { } 注释，外加 (* *)；字符串只有单引号，两个连写表示一个引号（靠同色相邻合并还原）
        private static readonly Syntax PascalStyle = new Syntax(
            "//", "{", "}",
            new[] { "'" },
            SetIgnoreCase("and array as asm begin case class const constructor destructor dispinterface "
                + "div do downto else end except exports file finalization finally for function goto if "
                + "implementation in inherited initialization inline interface is label library mod nil "
                + "not object of or out packed procedure program property raise record repeat resourcestring "
                + "set shl shr string then threadvar to try type unit until uses var while with xor "
                + "absolute abstract alias assembler cdecl default dispid dynamic export external far "
                + "forward local message near overload override pascal private protected public published "
                + "read readonly register reintroduce requires safecall stdcall stored virtual write "
                + "writeonly true false self result exit break continue strict"),
            SetIgnoreCase("integer cardinal int64 longint longword shortint smallint byte word dword "
                + "boolean bytebool wordbool longbool char ansichar widechar real single double extended "
                + "comp currency tdatetime variant oleVariant pointer tObject"),
            false, "(*", "*)");

        // Visual Basic：撇号注释，双引号里的两个引号表示一个引号；关键字不分大小写
        private static readonly Syntax BasicStyle = new Syntax(
            "'", null, null,
            new[] { "\"" },
            SetIgnoreCase("addhandler addressof alias and andalso as boolean byref byte byval call case catch "
                + "cbool cbyte cchar cdate cdbl cdec char cint class clng cobj const continue csbyte cshort "
                + "csng cstr ctype cuint culng cushort date decimal declare default delegate dim directcast do "
                + "double each else elseif end endif enum erase error event exit false finally for friend "
                + "function get gettype global goto handles if implements imports in inherits integer interface "
                + "is let lib like long loop me mod module mustinherit mustoverride mybase myclass namespace "
                + "narrowing new next not nothing notinheritable notoverridable object of on operator option "
                + "optional or orelse overloads overridable overrides paramarray partial private property "
                + "protected public raiseevent readonly redim rem removehandler resume return sbyte select set "
                + "shadows shared short single static step stop string structure sub syncLock then throw to true "
                + "try trycast typeof uinteger ulong ushort using variant wend when while widening with withevents "
                + "writeonly xor"),
            null);

        // CSS：只有 /* */ 注释，属性名靠「名字 + 冒号」认；@media 这种把 @ 留在正文色里
        private static readonly Syntax CssStyle = new Syntax(
            null, "/*", "*/",
            new[] { "\"", "'" },
            SetIgnoreCase("media import charset keyframes font-face supports page namespace document "
                + "important inherit initial unset revert auto none normal block inline flex grid table "
                + "absolute relative fixed sticky static transparent currentcolor solid dashed dotted "
                + "hidden visible scroll center left right top bottom nowrap wrap pointer"),
            null,
            false, null, null, null, true, false, true, null, false, false, false, false, false, true);

        // SCSS/Sass：CSS 那套再加上 // 行注释、$变量、@mixin/@include 这些指令
        private static readonly Syntax ScssStyle = new Syntax(
            "//", "/*", "*/",
            new[] { "\"", "'" },
            SetIgnoreCase("media import charset keyframes font-face supports use forward mixin include "
                + "extend function return if else each for while debug warn error at-root content "
                + "important inherit initial unset revert auto none normal block inline flex grid table "
                + "absolute relative fixed sticky static transparent currentcolor solid dashed dotted"),
            null,
            false, null, null, null, true, false, true, null, false, false, false, false, false, true);

        // YAML：键名靠冒号认，值是 true/false/null 这些；# 必须是行首或空白后才是注释（#003366 是颜色）
        private static readonly Syntax YamlStyle = new Syntax(
            "#", null, null,
            new[] { "\"", "'" },
            SetIgnoreCase("true false yes no on off null nil"),
            null,
            propertyColon: true,
            lineCommentAfterSpace: true,
            hyphenInIdent: true,
            yamlAnchors: true,
            blockScalar: true);

        // R：# 注释，<- 赋值照旧是普通符号，关键字与常用类型分开上色
        private static readonly Syntax RStyle = new Syntax(
            "#", null, null,
            new[] { "\"", "'" },
            Set("if else repeat while function for in next break TRUE FALSE NULL NA NaN Inf "
                + "library require return invisible stop warning message cat print source"),
            null);

        // Lua：-- 行注释与 --[[ ]] 块注释，字符串有引号和 [[ ]] 两种
        private static readonly Syntax LuaStyle = new Syntax(
            "--", "--[[", "]]",
            new[] { "\"", "'" },
            Set("and break do else elseif end false for function goto if in local nil not or repeat "
                + "return then true until while self"),
            null,
            false, null, null, new[] { new[] { "[[", "]]" } });

        // INI：; 和 # 都是注释，[database] 是段落名，key = value 的 key 是键名
        private static readonly Syntax IniStyle = new Syntax(
            ";", null, null,
            new[] { "\"", "'" },
            SetIgnoreCase("true false yes no on off null none"),
            null,
            false, null, null, null, false, false, true, "#", true, true);

        // TOML：[package] / [[bin]] 段落名、key = value、只有 # 注释、支持三引号多行串
        private static readonly Syntax TomlStyle = new Syntax(
            "#", null, null,
            new[] { "\"\"\"", "'''", "\"", "'" },
            SetIgnoreCase("true false"),
            null,
            false, null, null, null, false, false, true, null, true, true);

        // PowerShell：关键字/变量/cmdlet 都跟 shell 不一样，单独一族。
        // 注释是 # 与 <# #>，字符串另有 @"..."@ 和 @'...'@ 两种 here-string；
        // 关键字不分大小写，-eq/-like 这类比较运算符虽然带减号，减号留正文色、单词照关键字上色。
        private static readonly Syntax PwshStyle = new Syntax(
            "#", "<#", "#>",
            new[] { "\"", "'" },
            SetIgnoreCase("if elseif else switch foreach for while do until function filter param begin process end "
                + "return break continue throw try catch finally trap in not and or xor class enum using namespace "
                + "exit var set eq ne gt ge lt le like notlike match notmatch contains notcontains replace split "
                + "join is isnot as"),
            null,
            quotePairs: new[] { new[] { "@\"", "\"@" }, new[] { "@'", "'@" } },
            hyphenInIdent: true,
            dollarVariable: true,
            cmdletIdentifier: true);

        // Dockerfile：注释只在行首或空白后（RUN echo "#x" 里的 # 不算），指令是一批大写动词，
        // $VAR / ${VAR} 是变量，字符串照常
        private static readonly Syntax DockerStyle = new Syntax(
            "#", null, null,
            new[] { "\"", "'" },
            SetIgnoreCase("from run cmd label maintainer expose env add copy entrypoint volume user workdir "
                + "arg onbuild stopsignal healthcheck shell as"),
            null,
            lineCommentAfterSpace: true,
            dollarVariable: true);

        // Makefile：# 注释（允许行尾），行首「名字 + 冒号」是目标，$(CC)/$@ 这类是自动变量
        private static readonly Syntax MakeStyle = new Syntax(
            "#", null, null,
            new[] { "\"", "'" },
            SetIgnoreCase("include sinclude ifdef ifndef ifeq ifneq else endif export unexport define endef "
                + "override vpath undefine"),
            null,
            lineCommentAfterSpace: true,
            dollarVariable: true,
            makeTarget: true);

        /// <summary>
        /// 把代码切成带类型的高亮片段。language 是围栏上的语言标识（```python 里的 python），
        /// 空或不认识时整段返回一个 Plain，调用方按普通文本渲染即可。
        /// </summary>
        public static IReadOnlyList<CodeToken> Highlight(string code, string language)
        {
            var tokens = new List<CodeToken>();
            if (string.IsNullOrEmpty(code))
                return tokens;

            Syntax syntax = Resolve(language);
            if (syntax == null)
            {
                tokens.Add(new CodeToken(code, TokenKind.Plain));
                return tokens;
            }

            // 标记语言的结构（<div class="x">）关键字表表达不了，单独走一套扫描
            if (syntax.Markup)
                return Merge(HighlightMarkup(code));

            int i = 0;
            int n = code.Length;
            // CSS/SCSS 的选择器区：行首到本行第一个 '{' 之间。每翻一行重算一次，
            // 跨行的注释/字符串吃掉换行时置 -1（这一行就不做选择器判定，宁可少上色）
            int selectorEnd = -1;
            while (i < n)
            {
                if (syntax.CssSelectors && (i == 0 || code[i - 1] == '\n'))
                    selectorEnd = FindSelectorEnd(code, i);

                // 块注释必须排在行注释前面：Lua 的 --[[ 也会命中 -- 行注释前缀
                if (syntax.BlockOpen != null && Match(code, i, syntax.BlockOpen))
                {
                    int close = code.IndexOf(syntax.BlockClose, i + syntax.BlockOpen.Length,
                        StringComparison.Ordinal);
                    int end = close < 0 ? n : close + syntax.BlockClose.Length;
                    tokens.Add(new CodeToken(code.Substring(i, end - i), TokenKind.Comment));
                    if (code.IndexOf('\n', i, end - i) >= 0)
                        selectorEnd = -1;
                    i = end;
                    continue;
                }

                // 第二种块注释（Delphi 的 (* *)）
                if (syntax.BlockOpen2 != null && Match(code, i, syntax.BlockOpen2))
                {
                    int close = code.IndexOf(syntax.BlockClose2, i + syntax.BlockOpen2.Length,
                        StringComparison.Ordinal);
                    int end = close < 0 ? n : close + syntax.BlockClose2.Length;
                    tokens.Add(new CodeToken(code.Substring(i, end - i), TokenKind.Comment));
                    i = end;
                    continue;
                }

                if (syntax.LineComment != null && Match(code, i, syntax.LineComment)
                    && (!syntax.LineCommentAfterSpace || IsCommentStart(code, i)))
                {
                    int end = code.IndexOf('\n', i);
                    if (end < 0)
                        end = n;
                    tokens.Add(new CodeToken(code.Substring(i, end - i), TokenKind.Comment));
                    i = end;
                    continue;
                }

                if (syntax.LineComment2 != null && Match(code, i, syntax.LineComment2))
                {
                    int end = code.IndexOf('\n', i);
                    if (end < 0)
                        end = n;
                    tokens.Add(new CodeToken(code.Substring(i, end - i), TokenKind.Comment));
                    i = end;
                    continue;
                }

                // [database] / [[bin]]：只有行首（前面仅空白）的方括号才是段落名
                if (syntax.SectionBrackets && code[i] == '[' && IsLineStart(code, i))
                {
                    int sectionEnd = ScanSection(code, i);
                    if (sectionEnd > i)
                    {
                        tokens.Add(new CodeToken(code.Substring(i, sectionEnd - i), TokenKind.Section));
                        i = sectionEnd;
                        continue;
                    }
                }

                string pairClose = MatchQuotePair(syntax, code, i, out string pairOpen);
                if (pairClose != null)
                {
                    int end = ScanDelimited(code, i + pairOpen.Length, pairClose);
                    tokens.Add(new CodeToken(code.Substring(i, end - i), TokenKind.String));
                    i = end;
                    continue;
                }

                string quote = MatchQuote(syntax, code, i);
                if (quote != null)
                {
                    int end = ScanString(code, i, quote);
                    // JSON：引号串后面跟冒号的是键，跟值分开上色
                    TokenKind kind = syntax.QuotedKey && NextIsColon(code, end)
                        ? TokenKind.Property
                        : TokenKind.String;
                    tokens.Add(new CodeToken(code.Substring(i, end - i), kind));
                    if (code.IndexOf('\n', i, end - i) >= 0)
                        selectorEnd = -1;
                    i = end;
                    continue;
                }

                // YAML 的 &anchor / *alias
                if (syntax.YamlAnchors && (code[i] == '&' || code[i] == '*'))
                {
                    int anchorEnd = i + 1;
                    while (anchorEnd < n && IsIdentPart(code[anchorEnd]))
                        anchorEnd++;
                    if (anchorEnd > i + 1)
                    {
                        tokens.Add(new CodeToken(code.Substring(i, anchorEnd - i), TokenKind.Variable));
                        i = anchorEnd;
                        continue;
                    }
                }

                // YAML 的多行标量：行尾的 | 或 >，后面那些缩进行整段当字符串
                if (syntax.BlockScalar && (code[i] == '|' || code[i] == '>') && IsEndOfLine(code, i))
                {
                    int scalarEnd = ScanBlockScalar(code, i);
                    tokens.Add(new CodeToken(code.Substring(i, scalarEnd - i), TokenKind.String));
                    i = scalarEnd;
                    continue;
                }

                // 选择器里的 .class 与 #id：连点号/井号一起上色，看起来才是一个整体
                if (syntax.CssSelectors && i < selectorEnd && (code[i] == '.' || code[i] == '#'))
                {
                    int nameEnd = i + 1;
                    while (nameEnd < n && IsSelectorNameChar(code[nameEnd]))
                        nameEnd++;
                    if (nameEnd > i + 1)
                    {
                        tokens.Add(new CodeToken(code.Substring(i, nameEnd - i), TokenKind.Tag));
                        i = nameEnd;
                        continue;
                    }
                }

                // $VAR / ${VAR} / $(CC) / $@：Dockerfile、PowerShell、Makefile 的变量写法
                if (syntax.DollarVariable && code[i] == '$')                {
                    int dollarEnd = ScanDollar(code, i);
                    if (dollarEnd > i)
                    {
                        tokens.Add(new CodeToken(code.Substring(i, dollarEnd - i), TokenKind.Variable));
                        i = dollarEnd;
                        continue;
                    }
                }

                char c = code[i];
                if (IsIdentStart(c))
                {
                    int start = i;
                    i++;
                    while (i < n && IsIdentPart(code[i]))
                        i++;
                    // CSS / YAML 的连字符标识符：只在连字符后面还跟着标识符字符时才吃进来，
                    // 免得把 `a - b` 或行尾的 `-` 并进名字里
                    if (syntax.HyphenInIdent)
                    {
                        while (i + 1 < n && code[i] == '-' && IsIdentPart(code[i + 1]))
                        {
                            i++;
                            while (i < n && IsIdentPart(code[i]))
                                i++;
                        }
                    }
                    string word = code.Substring(start, i - start);
                    // CSS / SCSS / YAML：名字后面跟冒号的是属性名或键名，优先于关键字判断
                    TokenKind kind;
                    if (syntax.CssSelectors && start < selectorEnd)
                        kind = TokenKind.Tag;        // 选择器里的元素名、:hover 的伪类名
                    else if (syntax.CmdletIdentifier && word.IndexOf('-') > 0)
                        kind = TokenKind.Function;   // PowerShell 的 Get-ChildItem
                    else if (syntax.MakeTarget && IsLineStart(code, start) && NextIsMakeAssign(code, i))
                        kind = TokenKind.Property;   // Makefile 的 VAR := / VAR +=
                    else if (syntax.MakeTarget && IsLineStart(code, start) && i < n && code[i] == ':')
                        kind = TokenKind.Section;    // Makefile 的构建目标
                    else if (syntax.PropertyColon && NextIsColon(code, i))
                        kind = TokenKind.Property;
                    else if (syntax.PropertyEquals && IsLineStart(code, start) && NextIsAssign(code, i))
                        kind = TokenKind.Property;   // INI / TOML 的 key = value
                    else
                        kind = Classify(word, syntax);
                    // 关键字/类型后面也可能跟括号（if(...)），只有普通标识符才算函数名
                    if (kind == TokenKind.Plain && NextIsOpenParen(code, i))
                        kind = TokenKind.Function;
                    tokens.Add(new CodeToken(word, kind));
                    continue;
                }

                if (char.IsDigit(c))
                {
                    int start = i;
                    i++;
                    while (i < n && IsNumberPart(code, i, start))
                        i++;
                    tokens.Add(new CodeToken(code.Substring(start, i - start), TokenKind.Number));
                    continue;
                }

                tokens.Add(new CodeToken(code[i].ToString(), TokenKind.Plain));
                i++;
            }

            return Merge(tokens);
        }

        // ── 标记语言（HTML / XML / XAML / Vue）──────────────────────

        /// <summary>
        /// 标记语言的词法。标签名的位置只能从 &lt;...&gt; 的结构里看出来，所以不走通用扫描器：
        /// 注释 <!-- --> 整段记 Comment；遇到标签起点就交给 ScanTag，标签名记 Tag、属性名记 Attribute、
        /// 属性值按引号记 String；标签之外的正文一律 Plain（正文里的 "a &lt; b" 不会被当标签）。
        /// script / style 里的内容不做二级高亮（HTML 里嵌 JS/CSS 就按正文色显示）。
        /// </summary>
        private static List<CodeToken> HighlightMarkup(string code)
        {
            var tokens = new List<CodeToken>();
            int i = 0;
            int n = code.Length;
            while (i < n)
            {
                if (Match(code, i, "<!--"))
                {
                    int close = code.IndexOf("-->", i + 4, StringComparison.Ordinal);
                    int end = close < 0 ? n : close + 3;
                    tokens.Add(new CodeToken(code.Substring(i, end - i), TokenKind.Comment));
                    i = end;
                    continue;
                }

                if (code[i] == '<' && IsTagStart(code, i))
                {
                    i = ScanTag(code, i, tokens);
                    continue;
                }

                // 标签之外的正文：一直攒到下一个 '<'，落单的 '<'（a < b）也走这里
                int start = i;
                while (i < n && code[i] != '<')
                    i++;
                if (i == start)
                    i++;
                tokens.Add(new CodeToken(code.Substring(start, i - start), TokenKind.Plain));
            }
            return tokens;
        }

        /// <summary>i 指向 '&lt;'。后面紧跟标签名、'/'、'!'、'?' 才算标签开始，免得把正文里的 "a &lt; b" 当标签。</summary>
        private static bool IsTagStart(string code, int i)
        {
            int j = i + 1;
            if (j >= code.Length)
                return false;
            char c = code[j];
            if (c == '/' || c == '!' || c == '?')
                return true;
            return IsTagNameChar(c, true);
        }

        /// <summary>
        /// 扫一个标签：从 '&lt;' 到配对的 '&gt;'（含自闭合的 '/&gt;'），把片段填进 tokens，返回 '&gt;' 之后的位置。
        /// 引号里的 '&gt;' 不算标签结束；标签一直没闭合就扫到结尾，不会卡住。
        /// </summary>
        private static int ScanTag(string code, int start, List<CodeToken> tokens)
        {
            int n = code.Length;
            int i = start;

            tokens.Add(new CodeToken("<", TokenKind.Plain));
            i++;
            // 声明（<!DOCTYPE / <?xml）里带 = 的才是属性，不带 = 的是根元素名，按标签上色
            bool declaration = false;
            if (i < n && code[i] == '/')
            {
                tokens.Add(new CodeToken("/", TokenKind.Plain));
                i++;
            }
            else if (i < n && (code[i] == '!' || code[i] == '?'))
            {
                declaration = true;
                tokens.Add(new CodeToken(code[i].ToString(), TokenKind.Plain));
                i++;
            }

            int nameStart = i;
            while (i < n && IsTagNameChar(code[i], false))
                i++;
            if (i > nameStart)
                tokens.Add(new CodeToken(code.Substring(nameStart, i - nameStart), TokenKind.Tag));

            while (i < n)
            {
                char c = code[i];

                if (c == '>')
                {
                    tokens.Add(new CodeToken(">", TokenKind.Plain));
                    return i + 1;
                }

                // 空白和自闭合的 '/' 都算正文色，交给 Merge 归并
                if (char.IsWhiteSpace(c) || c == '/')
                {
                    int wsStart = i;
                    while (i < n && (char.IsWhiteSpace(code[i]) || code[i] == '/'))
                        i++;
                    tokens.Add(new CodeToken(code.Substring(wsStart, i - wsStart), TokenKind.Plain));
                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    int end = ScanString(code, i, c.ToString());
                    tokens.Add(new CodeToken(code.Substring(i, end - i), TokenKind.String));
                    i = end;
                    continue;
                }

                if (c == '=')
                {
                    tokens.Add(new CodeToken("=", TokenKind.Plain));
                    i = ScanAttributeValue(code, i + 1, tokens);
                    continue;
                }

                if (IsAttributeNameChar(c, true))
                {
                    int attrStart = i;
                    while (i < n && IsAttributeNameChar(code[i], false))
                        i++;
                    // 后面跟 = 的是属性名；声明里不带 = 的词（<!DOCTYPE html> 里的 html）是根元素名
                    TokenKind kind = declaration && !NextIsAssign(code, i)
                        ? TokenKind.Tag
                        : TokenKind.Attribute;
                    tokens.Add(new CodeToken(code.Substring(attrStart, i - attrStart), kind));
                    continue;
                }

                tokens.Add(new CodeToken(c.ToString(), TokenKind.Plain));
                i++;
            }

            return n;
        }

        /// <summary>
        /// '=' 后面的值。带引号的留给 ScanTag 主循环按 String 处理；这里接不带引号的写法
        /// （width=100、href=/a/b.html），纯数字的上数字色，其余按正文色。
        /// </summary>
        private static int ScanAttributeValue(string code, int i, List<CodeToken> tokens)
        {
            int n = code.Length;
            // 值前面可能隔着空白（width= 100），这段空白也得原样吐出来，否则渲染会少字符
            int wsStart = i;
            while (i < n && char.IsWhiteSpace(code[i]))
                i++;
            if (i > wsStart)
                tokens.Add(new CodeToken(code.Substring(wsStart, i - wsStart), TokenKind.Plain));
            if (i >= n)
                return i;

            char c = code[i];
            if (c == '"' || c == '\'')
                return i;

            int start = i;
            while (i < n && !char.IsWhiteSpace(code[i]) && code[i] != '>')
                i++;
            if (i == start)
                return i;

            string value = code.Substring(start, i - start);
            tokens.Add(new CodeToken(value, IsNumeric(value) ? TokenKind.Number : TokenKind.Plain));
            return i;
        }

        /// <summary>标签名的字符集。XAML 的 Grid.Row、Vue 的 my-component、XML 的 xlink:href 都要认。</summary>
        private static bool IsTagNameChar(char c, bool first)
        {
            if (char.IsLetter(c))
                return true;
            if (first)
                return c == '_' || c == ':';
            return char.IsDigit(c) || c == '_' || c == '-' || c == '.' || c == ':';
        }

        /// <summary>
        /// 属性名的字符集，比标签名宽：Vue 的 :value / @click / v-if、Angular 的 *ngIf / (click)、
        /// Svelte 的 on:click，都是靠这些符号开头的。
        /// </summary>
        private static bool IsAttributeNameChar(char c, bool first)
        {
            if (char.IsLetter(c))
                return true;
            if (first)
                return c == '_' || c == ':' || c == '@' || c == '#' || c == '*' || c == '[' || c == '(';
            return char.IsDigit(c) || c == '_' || c == '-' || c == '.' || c == ':' || c == '@'
                || c == ']' || c == ')';
        }

        /// <summary>整段都是数字（含小数点和下划线分隔）才算数字，别把 2px 当数字。</summary>
        private static bool IsNumeric(string value)
        {
            if (value.Length == 0)
                return false;
            for (int k = 0; k < value.Length; k++)
            {
                char c = value[k];
                if (!char.IsDigit(c) && c != '.' && c != '_')
                    return false;
            }
            return true;
        }

        /// <summary>相邻的同类型片段并成一个，少生成一堆 Run。</summary>
        private static List<CodeToken> Merge(List<CodeToken> tokens)
        {
            var merged = new List<CodeToken>(tokens.Count);
            for (int i = 0; i < tokens.Count; i++)
            {
                CodeToken token = tokens[i];
                int last = merged.Count - 1;
                if (last >= 0 && merged[last].Kind == token.Kind)
                    merged[last] = new CodeToken(merged[last].Text + token.Text, token.Kind);
                else
                    merged.Add(token);
            }
            return merged;
        }

        /// <summary>语言标识映射到词法规则。只认第一个词，```python title=xx 这种带参数的也认。</summary>
        public static Syntax Resolve(string language)
        {
            if (string.IsNullOrWhiteSpace(language))
                return null;

            string name = language.Trim().ToLowerInvariant();
            int stop = name.IndexOfAny(new[] { ' ', '\t', '{', ';' });
            if (stop >= 0)
                name = name.Substring(0, stop);

            switch (name)
            {
                case "c":
                case "h":
                case "cpp":
                case "c++":
                case "cc":
                case "hpp":
                case "cs":
                case "csharp":
                case "java":
                case "js":
                case "javascript":
                case "jsx":
                case "ts":
                case "typescript":
                case "tsx":
                case "go":
                case "golang":
                case "rust":
                case "rs":
                case "php":
                case "swift":
                case "kt":
                case "kotlin":
                case "scala":
                case "dart":
                case "groovy":
                case "objc":
                case "objective-c":
                    return CStyle;

                case "py":
                case "python":
                case "python2":
                case "python3":
                    return PythonStyle;

                case "sh":
                case "bash":
                case "shell":
                case "zsh":
                case "console":
                case "conf":
                case "bat":
                case "cmd":
                case "ruby":
                case "rb":
                case "perl":
                case "pl":
                    return HashStyle;

                case "yaml":
                case "yml":
                    return YamlStyle;

                case "ini":
                case "inf":
                case "properties":
                case "editorconfig":
                    return IniStyle;

                case "toml":
                    return TomlStyle;

                case "powershell":
                case "ps1":
                case "pwsh":
                case "psm1":
                    return PwshStyle;

                case "dockerfile":
                case "docker":
                case "containerfile":
                    return DockerStyle;

                case "makefile":
                case "make":
                case "mk":
                case "gnumakefile":
                    return MakeStyle;

                case "pas":
                case "pascal":
                case "delphi":
                case "objectpascal":
                case "dpr":
                case "dpk":
                case "inc":
                    return PascalStyle;

                case "vb":
                case "vbnet":
                case "visualbasic":
                case "vbs":
                case "vba":
                case "bas":
                case "cls":
                case "frm":
                    return BasicStyle;

                case "css":
                    return CssStyle;

                case "scss":
                case "sass":
                case "less":
                    return ScssStyle;

                case "r":
                case "rlang":
                case "rscript":
                    return RStyle;

                case "lua":
                case "luau":
                    return LuaStyle;

                case "sql":
                case "mysql":
                case "postgresql":
                case "postgres":
                case "sqlite":
                case "tsql":
                    return SqlStyle;

                case "json":
                case "jsonc":
                    return JsonStyle;

                case "html":
                case "htm":
                case "xhtml":
                case "xml":
                case "xaml":
                case "svg":
                case "vue":
                    return MarkupStyle;

                default:
                    return null;
            }
        }

        /// <summary>语言标识映射到词法族，认不出返回 None。</summary>
        public static SyntaxFamily FamilyOf(string language)
        {
            Syntax syntax = Resolve(language);
            if (syntax == null)
                return SyntaxFamily.None;
            if (ReferenceEquals(syntax, CStyle))
                return SyntaxFamily.CStyle;
            if (ReferenceEquals(syntax, PythonStyle))
                return SyntaxFamily.Python;
            if (ReferenceEquals(syntax, HashStyle))
                return SyntaxFamily.Hash;
            if (ReferenceEquals(syntax, SqlStyle))
                return SyntaxFamily.Sql;
            if (ReferenceEquals(syntax, JsonStyle))
                return SyntaxFamily.Json;
            if (ReferenceEquals(syntax, MarkupStyle))
                return SyntaxFamily.Markup;
            if (ReferenceEquals(syntax, PascalStyle))
                return SyntaxFamily.Pascal;
            if (ReferenceEquals(syntax, BasicStyle))
                return SyntaxFamily.Basic;
            if (ReferenceEquals(syntax, CssStyle))
                return SyntaxFamily.Css;
            if (ReferenceEquals(syntax, ScssStyle))
                return SyntaxFamily.Scss;
            if (ReferenceEquals(syntax, YamlStyle))
                return SyntaxFamily.Yaml;
            if (ReferenceEquals(syntax, RStyle))
                return SyntaxFamily.R;
            if (ReferenceEquals(syntax, LuaStyle))
                return SyntaxFamily.Lua;
            if (ReferenceEquals(syntax, IniStyle) || ReferenceEquals(syntax, TomlStyle))
                return SyntaxFamily.Ini;
            if (ReferenceEquals(syntax, PwshStyle))
                return SyntaxFamily.PowerShell;
            if (ReferenceEquals(syntax, DockerStyle))
                return SyntaxFamily.Dockerfile;
            if (ReferenceEquals(syntax, MakeStyle))
                return SyntaxFamily.Makefile;
            return SyntaxFamily.None;
        }

        private static TokenKind Classify(string word, Syntax syntax)
        {
            if (syntax.Types != null && syntax.Types.Contains(word))
                return TokenKind.Type;
            if (syntax.Keywords != null && syntax.Keywords.Contains(word))
                return TokenKind.Keyword;
            return TokenKind.Plain;
        }

        /// <summary>从 i 处开始匹配引号，返回引号本身；不在引号起点返回 null。</summary>
        private static string MatchQuote(Syntax syntax, string code, int i)
        {
            if (syntax.Quotes == null)
                return null;
            for (int q = 0; q < syntax.Quotes.Length; q++)
            {
                if (Match(code, i, syntax.Quotes[q]))
                    return syntax.Quotes[q];
            }
            return null;
        }

        /// <summary>匹配「开闭不同」的成对引号（Lua 的 [[ ]]），命中时给出闭合记号。</summary>
        private static string MatchQuotePair(Syntax syntax, string code, int i, out string open)
        {
            open = null;
            if (syntax.QuotePairs == null)
                return null;
            for (int q = 0; q < syntax.QuotePairs.Length; q++)
            {
                string[] pair = syntax.QuotePairs[q];
                if (pair != null && pair.Length == 2 && Match(code, i, pair[0]))
                {
                    open = pair[0];
                    return pair[1];
                }
            }
            return null;
        }

        /// <summary>从 start 扫到成对引号的闭合处，返回闭合记号之后的位置；跨行。</summary>
        private static int ScanDelimited(string code, int start, string close)
        {
            int at = code.IndexOf(close, start, StringComparison.Ordinal);
            return at < 0 ? code.Length : at + close.Length;
        }

        /// <summary>
        /// 扫 $ 形式的变量：${VAR}、$(CC)、$@/$&lt;/$^、$PATH。
        /// 返回结束位置；不构成变量（孤零零一个 $）返回 -1。
        /// </summary>
        private static int ScanDollar(string code, int i)
        {
            int n = code.Length;
            int j = i + 1;
            if (j >= n)
                return -1;

            if (code[j] == '{' || code[j] == '(')
            {
                char close = code[j] == '{' ? '}' : ')';
                int at = code.IndexOf(close, j + 1);
                return at < 0 ? -1 : at + 1;
            }
            if ("@<^?*%".IndexOf(code[j]) >= 0)
                return j + 1;

            while (j < n && (char.IsLetterOrDigit(code[j]) || code[j] == '_' || code[j] == ':'))
                j++;
            return j > i + 1 ? j : -1;
        }

        /// <summary>Makefile 的赋值号：=/:=/?=/+=/!=（跳过空白后判断）。</summary>
        private static bool NextIsMakeAssign(string code, int i)
        {
            while (i < code.Length && (code[i] == ' ' || code[i] == '\t'))
                i++;
            if (i >= code.Length)
                return false;
            if (code[i] == '=')
                return true;
            return (code[i] == ':' || code[i] == '?' || code[i] == '+' || code[i] == '!')
                && i + 1 < code.Length && code[i + 1] == '=';
        }

        /// <summary>
        /// 找这一行选择器区的结束位置：本行第一个 '{' 之前算选择器；本行没有 '{' 但以逗号结尾
        /// （多行选择器列表）则整行都算；其余（普通声明行、空行）返回 -1。
        /// at-rule（@media、@include）不开选择器区，免得把 max-width: 当成选择器。
        /// </summary>
        private static int FindSelectorEnd(string code, int lineStart)
        {
            int n = code.Length;
            int i = lineStart;
            while (i < n && (code[i] == ' ' || code[i] == '\t'))
                i++;
            if (i >= n || code[i] == '\n' || code[i] == '\r' || code[i] == '@')
                return -1;

            int lineEnd = i;
            while (lineEnd < n && code[lineEnd] != '\n')
            {
                if (code[lineEnd] == '{')
                    return lineEnd;
                lineEnd++;
            }

            int last = lineEnd - 1;
            while (last >= i && (code[last] == ' ' || code[last] == '\t' || code[last] == '\r'))
                last--;
            return last >= i && code[last] == ',' ? lineEnd : -1;
        }

        /// <summary>选择器名字里的字符：.btn、#main、.my-class 都靠它判断结尾。</summary>
        private static bool IsSelectorNameChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_' || c == '-';
        }

        /// <summary>i 处（| 或 &gt;）到行尾之间只剩空白，才算多行标量的起始。</summary>
        private static bool IsEndOfLine(string code, int i)
        {
            int j = i + 1;
            while (j < code.Length && (code[j] == ' ' || code[j] == '\t'))
                j++;
            return j >= code.Length || code[j] == '\n' || code[j] == '\r';
        }

        /// <summary>
        /// 从 | 或 &gt; 扫到多行标量结束：后面凡是空行或带缩进的行都算标量内容，
        /// 遇到第一个顶格的非空行就停（那是下一个键）。
        /// </summary>
        private static int ScanBlockScalar(string code, int i)
        {
            int n = code.Length;
            int j = code.IndexOf('\n', i);
            if (j < 0)
                return n;
            j++;

            int end = j;
            while (j < n)
            {
                int lineEnd = code.IndexOf('\n', j);
                if (lineEnd < 0)
                    lineEnd = n;
                if (!IsIndentedOrBlank(code, j, lineEnd))
                    break;
                end = lineEnd < n ? lineEnd + 1 : n;
                j = end;
            }
            return end;
        }

        /// <summary>这一行是空行或以空白开头（多行标量的内容行）。</summary>
        private static bool IsIndentedOrBlank(string code, int start, int end)
        {
            if (end <= start)
                return true;
            char c = code[start];
            return c == ' ' || c == '\t' || c == '\r' || c == '\n';
        }

        /// <summary>i 处是不是行注释起点：行首，或者前一个字符是空白（YAML 用）。</summary>
        private static bool IsCommentStart(string code, int i)
        {
            if (i <= 0)
                return true;
            char p = code[i - 1];
            return p == '\n' || p == '\r' || p == ' ' || p == '\t';
        }

        /// <summary>i 前面只有空白（或已在文件开头），说明它在行首。</summary>
        private static bool IsLineStart(string code, int i)
        {
            int j = i - 1;
            while (j >= 0 && (code[j] == ' ' || code[j] == '\t'))
                j--;
            return j < 0 || code[j] == '\n' || code[j] == '\r';
        }

        /// <summary>
        /// 从 '[' 扫到同一行的 ']'（TOML 的 [[bin]] 要两个），返回段落名结束位置；
        /// 不跨行，这一行里找不到闭合就返回 -1（当成普通正文）。
        /// </summary>
        private static int ScanSection(string code, int i)
        {
            int n = code.Length;
            int j = i + 1;
            bool doubled = j < n && code[j] == '[';
            if (doubled)
                j++;
            while (j < n && code[j] != '\n')
            {
                if (code[j] == ']')
                {
                    if (!doubled)
                        return j + 1;
                    if (j + 1 < n && code[j + 1] == ']')
                        return j + 2;
                }
                j++;
            }
            return -1;
        }

        /// <summary>标识符后面（跳过空格）紧跟冒号，且不是双冒号（C++/PHP 的 ::）。</summary>
        private static bool NextIsColon(string code, int i)
        {
            while (i < code.Length && (code[i] == ' ' || code[i] == '\t'))
                i++;
            return i < code.Length && code[i] == ':'
                && (i + 1 >= code.Length || code[i + 1] != ':');
        }

        /// <summary>
        /// 从引号起点扫到闭合引号，返回结束位置（含闭合引号）。单字符引号不跨行，扫到行尾就收；
        /// 三引号和反引号（JS 模板串）可以跨行。反斜杠转义跳过下一个字符。
        /// </summary>
        private static int ScanString(string code, int start, string quote)
        {
            bool multiline = quote.Length >= 3 || quote == "`";
            int i = start + quote.Length;
            while (i < code.Length)
            {
                char c = code[i];
                if (c == '\\' && i + 1 < code.Length)
                {
                    i += 2;
                    continue;
                }
                if (Match(code, i, quote))
                    return i + quote.Length;
                if (c == '\n' && !multiline)
                    return i;
                i++;
            }
            return code.Length;
        }

        private static bool Match(string code, int i, string token)
        {
            return i + token.Length <= code.Length
                && string.CompareOrdinal(code, i, token, 0, token.Length) == 0;
        }

        /// <summary>标识符后面（跳过空格）紧跟左括号，当成函数名。</summary>
        private static bool NextIsOpenParen(string code, int i)
        {
            while (i < code.Length && (code[i] == ' ' || code[i] == '\t'))
                i++;
            return i < code.Length && code[i] == '(';
        }

        /// <summary>标识符后面（跳过空格）紧跟等号，说明它是属性名而不是根元素名。</summary>
        private static bool NextIsAssign(string code, int i)
        {
            while (i < code.Length && (code[i] == ' ' || code[i] == '\t'))
                i++;
            return i < code.Length && code[i] == '=';
        }

        private static bool IsIdentStart(char c)
        {
            return char.IsLetter(c) || c == '_' || c == '$';
        }

        private static bool IsIdentPart(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_' || c == '$';
        }

        /// <summary>
        /// 数字里可以带小数点、下划线分隔符、十六进制字母；e/E 后面的正负号也算数字的一部分
        /// （1e-3），别的地方的 +- 不算，免得把 1+2 吃成一个数。
        /// </summary>
        private static bool IsNumberPart(string code, int i, int start)
        {
            char c = code[i];
            if (char.IsLetterOrDigit(c) || c == '.' || c == '_')
                return true;
            return (c == '+' || c == '-') && i > start
                && (code[i - 1] == 'e' || code[i - 1] == 'E');
        }

        private static HashSet<string> Set(string words)
        {
            return new HashSet<string>(
                words.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.Ordinal);
        }

        private static HashSet<string> SetIgnoreCase(string words)
        {
            return new HashSet<string>(
                words.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.OrdinalIgnoreCase);
        }
    }
}

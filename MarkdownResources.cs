using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Security;

namespace MarkdownKit
{
    /// <summary>宿主为某张网络图片解析出的请求信息。</summary>
    public sealed class MarkdownImageRequest
    {
        /// <summary>实际要下载的地址（例如把 /blob/ 换成 raw），null 表示沿用原地址。</summary>
        public string Url;

        /// <summary>Authorization 头的值，null 表示匿名下载。</summary>
        public string Authorization;
    }

    /// <summary>
    /// 渲染层与宿主之间的零散接口：界面文案、私有图片源的认证、自签名证书策略。
    /// 宿主可在启动时改写；不设置则用内置中文文案、匿名下载、严格校验证书。
    /// </summary>
    public static class MarkdownResources
    {
        private static readonly Dictionary<string, string> DefaultTexts = new Dictionary<string, string>
        {
            { "copyCode", "复制代码" },
            { "codeLineNumbers", "显示行号" },
            { "footnoteJump", "跳到脚注 {0}" },
            { "footnoteBack", "回到正文引用处" }
        };

        /// <summary>文案翻译函数，key 即 <see cref="T"/> 的参数。返回空则回退内置文案。</summary>
        public static Func<string, string> Translate;

        /// <summary>按图片地址决定是否带认证、是否改写地址。null 表示全部匿名原样下载。</summary>
        public static Func<string, MarkdownImageRequest> ResolveImage;

        /// <summary>下载网络图片时的证书校验回调。null 表示按系统规则校验。</summary>
        public static RemoteCertificateValidationCallback AcceptCertificate;

        public static string T(string key)
        {
            var translate = Translate;
            if (translate != null)
            {
                string text = translate(key);
                if (!string.IsNullOrEmpty(text))
                    return text;
            }
            string fallback;
            return DefaultTexts.TryGetValue(key, out fallback) ? fallback : key;
        }
    }
}

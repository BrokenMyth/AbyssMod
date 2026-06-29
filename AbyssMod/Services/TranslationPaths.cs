using System;
using System.Collections.Generic;
using System.IO;

namespace AbyssMod.Services;

/// <summary>
/// 翻译资源路径构建工具。
/// 负责生成远程 URL 和本地缓存路径。
/// </summary>
public static class TranslationPaths
{
    public const string Manifest = "manifest";
    public const string Novels = "novels";
    public const string Static = "static";
    public const string UiTexts = "ui_texts";

    /// <summary>
    /// 需要加载的静态翻译资源类型。
    /// MasterData 表名来自 master_mapping.json 的 tables，扁平表来自 flat_types。
    /// </summary>
    public static IReadOnlyList<string> ContentTypes { get; set; } = Array.Empty<string>();

    /// <summary>由 MasterMapping 加载时调用，填入从 JSON 读到的资源清单。</summary>
    public static void SetContentTypes(List<string> types) => ContentTypes = types;

    /// <summary>
    /// 构建远程资源 URL。
    /// </summary>
    /// <param name="cdn">CDN 根地址（已去除尾部斜杠）。</param>
    /// <param name="type">资源类型。</param>
    /// <param name="language">语言代码。</param>
    /// <param name="id">可选的资源 ID（仅 novels 需要）。</param>
    /// <returns>完整的远程 URL。</returns>
    public static string BuildRemoteUrl(string cdn, string type, string language, string id = null)
    {
        // novels 是唯一带 id 的特例
        if (type == Novels)
        {
            if (id == null)
                throw new ArgumentException("Novel ID is required for novels type");
            return $"{cdn}/{Novels}/{id}/{language}.json";
        }

        if (type == Manifest)
            return $"{cdn}/{Manifest}/{language}.json";

        return $"{cdn}/{type}/{language}.json";
    }

    /// <summary>
    /// 构建本地缓存文件路径。
    /// </summary>
    /// <param name="cacheDir">缓存根目录。</param>
    /// <param name="type">资源类型。</param>
    /// <param name="language">语言代码。</param>
    /// <param name="id">可选的资源 ID（仅 novels 需要）。</param>
    /// <returns>完整的本地缓存路径。</returns>
    public static string BuildCachePath(
        string cacheDir,
        string type,
        string language,
        string id = null
    )
    {
        var langDir = Path.Combine(cacheDir, language);

        if (type == Novels)
        {
            if (id == null)
                throw new ArgumentException("Novel ID is required for novels type");
            return Path.Combine(langDir, Novels, $"{id}.json");
        }

        if (type == Manifest)
            return Path.Combine(langDir, $"{Manifest}.json");

        return Path.Combine(langDir, $"{type}.json");
    }
}

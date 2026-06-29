using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using TMPro;
using Utility.Fonts;
using Utility.Toast;

namespace AbyssMod.Services;

/// <summary>
/// 翻译管理器：协调翻译数据的加载、缓存和查询。
/// 内部持有所有翻译数据。
/// </summary>
public class TranslationManager
{
    private readonly TranslationCache _cache;
    private readonly FontHelper _font;
    private readonly object _loadLock = new();
    private Task _loadTask;

    private readonly ConcurrentDictionary<string, Task> _loadingNovels = new();

    /// <summary>
    /// 所有已加载的静态翻译表。结构为 {类型名: {字段名: {原文: 译文}}}。
    /// MasterData 走字段级查询，UI/剧情辅助表可继续通过 GetTable 获取扁平视图。
    /// </summary>
    private readonly Dictionary<string, Dictionary<string, Dictionary<string, string>>> _tables =
        new();

    private readonly Dictionary<string, Dictionary<string, string>> _flatTables = new();

    /// <summary>剧情正文翻译表，键为 novelId。结构特异（按需懒加载），独立存放。</summary>
    public ConcurrentDictionary<string, Dictionary<string, string>> Novels { get; private set; } =
        new();

    public FontHelper Font => _font;

    public TranslationManager(TranslationCache cache, FontHelper font)
    {
        _cache = cache;
        _font = font;
    }

    public void Initialize()
    {
        Plugin.Instance.StartCoroutine(
            _font
                .LoadAsync(() =>
                {
                    Logger.Info($"Font loaded: {_font.Asset.name}");
                    TMP_Settings.fallbackFontAssets.Add(_font.Asset);
                })
                .WrapToIl2Cpp()
        );
        _ = EnsureStaticTranslationsLoadedAsync();
    }

    public Task EnsureStaticTranslationsLoadedAsync()
    {
        lock (_loadLock)
        {
            return _loadTask ??= LoadTranslationAsync();
        }
    }

    public void EnsureStaticTranslationsLoaded()
    {
        EnsureStaticTranslationsLoadedAsync().GetAwaiter().GetResult();
    }

    public async Task LoadTranslationAsync()
    {
        if (!Config.Translation.Value)
            return;

        await _cache.FetchManifestAsync();

        var bundle = await _cache.LoadStaticBundleAsync();
        if (bundle == null)
        {
            Logger.Warn("MasterData static translation bundle load failed.");
            Toast.Warn("加载失败", "MasterData 静态翻译合并包加载失败");
            await LoadFlatStaticTablesAsync();
            return;
        }

        int total = 0;
        int loaded = 0;
        int missing = 0;
        foreach (var type in TranslationPaths.ContentTypes)
        {
            if (!IsMasterDataStaticType(type))
                continue;

            if (bundle.TryGetValue(type, out var table) && table != null)
            {
                _tables[type] = table;
                total += CountEntries(table);
                loaded++;
                _flatTables[type] = FlattenFields(table);
            }
            else
            {
                missing++;
            }
        }

        Logger.Info($"Static translation bundle loaded. Tables: {loaded}, Total: {total}");
        if (missing > 0)
            Logger.Warn($"Static translation bundle missing {missing} configured tables.");

        await LoadFlatStaticTablesAsync();
    }

    /// <summary>
    /// 查询指定类型的扁平翻译表。用于 UI 文本、剧情标题/人名等不携带 MasterData 字段名的调用点。
    /// </summary>
    public Dictionary<string, string> GetTable(string type)
    {
        return _flatTables.TryGetValue(type, out var table) ? table : null;
    }

    /// <summary>
    /// 查询指定类型、指定 MasterData 字段的翻译表。
    /// </summary>
    public Dictionary<string, string> GetFieldTable(string type, string field)
    {
        if (_tables.TryGetValue(type, out var fields) && fields.TryGetValue(field, out var table))
            return table;

        return GetTable(type);
    }

    private async Task LoadFlatStaticTablesAsync()
    {
        var tasks = new Dictionary<string, Task<Dictionary<string, string>>>();
        foreach (var type in TranslationPaths.ContentTypes)
            if (!IsMasterDataStaticType(type))
                tasks[type] = _cache.LoadAsync(type);

        if (tasks.Count == 0)
            return;

        await Task.WhenAll(tasks.Values);

        foreach (var (type, task) in tasks)
        {
            if (task.Result != null)
            {
                _flatTables[type] = task.Result;
                Logger.Info($"Flat static translation loaded [{type}]. Total: {task.Result.Count}");
            }
            else
            {
                Logger.Warn($"Flat static translation load failed [{type}]");
            }
        }
    }

    private static int CountEntries(Dictionary<string, Dictionary<string, string>> fields)
    {
        int count = 0;
        foreach (var table in fields.Values)
            if (table != null)
                count += table.Count;
        return count;
    }

    private static Dictionary<string, string> FlattenFields(
        Dictionary<string, Dictionary<string, string>> fields
    )
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var table in fields.Values)
        {
            if (table == null)
                continue;

            foreach (var (key, value) in table)
                result[key] = value;
        }
        return result;
    }

    private static bool IsMasterDataStaticType(string type) =>
        type.StartsWith("m_", StringComparison.Ordinal);

    public async Task GetNovelTranslationAsync(string novelId)
    {
        if (Novels.ContainsKey(novelId))
            return;

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var existingTask = _loadingNovels.GetOrAdd(novelId, tcs.Task);

        if (existingTask != tcs.Task)
        {
            await existingTask;
            return;
        }

        try
        {
            var translations = await _cache.LoadAsync(TranslationPaths.Novels, novelId);
            if (translations != null)
            {
                Novels[novelId] = translations;
                Logger.Info($"Scenario translation loaded. Total: {translations.Count}");
            }
            else
            {
                Logger.Warn($"Translations loaded failed: {novelId}");
                Toast.Warn("加载失败", $"剧本ID: {novelId}");
            }
            tcs.SetResult();
        }
        catch (Exception ex)
        {
            tcs.SetException(ex);
            throw;
        }
        finally
        {
            _loadingNovels.TryRemove(novelId, out _);
        }
    }
}

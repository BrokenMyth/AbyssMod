using AbyssMod.Services;
using HarmonyLib;
using Il2CppSystem;
using Il2CppSystem.Collections.Generic;
using Il2CppSystem.Threading;
using Project.Library;
using Project.MainStory;
using Project.Novel;
using Project.Outgame;
using TMPro;
using UnityEngine;


namespace AbyssMod.Patches;

/// <summary>
/// 剧情翻译补丁：标题、人名、对话文本的翻译注入。
/// </summary>
[HarmonyPatch]
public static class TranslationPatch
{
    private static NovelController _novelController;
    private static bool _uiTextsLoadRequested;
    private static bool _uiTextErrorLogged;

    private static string NovelId
    {
        get => _novelController?._common?.ScriptId ?? string.Empty;
    }

    private static string TranslateUiText(TMP_Text text, string value)
    {
        if (!Config.Translation.Value || Plugin.Trans == null || value == null)
            return value;

        if (value.Length == 0)
            return value;

        var uiTexts = GetUiTextTable();
        if (uiTexts == null || uiTexts.Count == 0)
            return value;

        if (IsTranslatedUiText(uiTexts, value))
            return value;

        if (uiTexts.TryGetValue(value, out string translated) && !string.IsNullOrEmpty(translated))
            return translated;

        string path = GetTransformPath(text?.transform);
        if (
            !string.IsNullOrEmpty(path)
            && uiTexts.TryGetValue(path, out translated)
            && !string.IsNullOrEmpty(translated)
        )
            return translated;

        return value;
    }

    private static bool IsTranslatedUiText(
        System.Collections.Generic.Dictionary<string, string> uiTexts,
        string value
    )
    {
        foreach (var translation in uiTexts.Values)
        {
            if (string.Equals(translation, value, System.StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static System.Collections.Generic.Dictionary<string, string> GetUiTextTable()
    {
        if (!_uiTextsLoadRequested)
        {
            _uiTextsLoadRequested = true;
            try
            {
                _ = Plugin.Trans.EnsureStaticTranslationsLoadedAsync();
            }
            catch (System.Exception e)
            {
                Logger.Warn($"UI text translation load request skipped: {e.Message}");
            }
        }

        return Plugin.Trans.GetTable(TranslationPaths.UiTexts);
    }

    private static string GetTransformPath(Transform transform)
    {
        if (transform == null)
            return null;

        var names = new System.Collections.Generic.Stack<string>();
        for (var current = transform; current != null; current = current.parent)
            names.Push(current.name);

        return string.Join("/", names);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NovelController), nameof(NovelController.InitNovel))]
    public static void InitNovelController(NovelController __instance)
    {
        _novelController = __instance;
    }

    public static bool TryGetCurrentNovel(
        out System.Collections.Generic.Dictionary<string, string> translation
    )
    {
        translation = null;
        return Config.Translation.Value
            && Plugin.Trans.Novels.TryGetValue(NovelId, out translation);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NovelPathUtility), nameof(NovelPathUtility.GetNovelScenarioDirectory))]
    public static void SetupTranslation(string novelId)
    {
        if (!Config.Translation.Value)
            return;

        Plugin.Log.LogInfo($"NovelId: {novelId}");

        Plugin.Trans.GetNovelTranslationAsync(novelId).Wait();
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NovelScriptInfoUtility), nameof(NovelScriptInfoUtility.GetScriptInfo))]
    public static void SetTitleAndDescription(ValueTuple<string, string> __result)
    {
        if (TryGetCurrentNovel(out var _))
        {
            string title = __result.Item1;
            var titles = Plugin.Trans.GetTable("titles");
            if (
                !string.IsNullOrEmpty(title)
                && titles != null
                && titles.TryGetValue(title, out string tTitle)
            )
                __result.Item1 = tTitle;

            string description = __result.Item2;
            var descriptions = Plugin.Trans.GetTable("descriptions");
            if (
                !string.IsNullOrEmpty(description)
                && descriptions != null
                && descriptions.TryGetValue(description, out string tDescription)
            )
                __result.Item2 = tDescription;
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NovelTitle), nameof(NovelTitle.SetTitle))]
    public static void SetTitle(ref string title)
    {
        if (TryGetCurrentNovel(out var _))
        {
            var titles = Plugin.Trans.GetTable("titles");
            if (
                !string.IsNullOrEmpty(title)
                && titles != null
                && titles.TryGetValue(title, out string tTitle)
            )
                title = tTitle;
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NovelViewMessageWindow), nameof(NovelViewMessageWindow.SetName))]
    public static void SetName(ref string name)
    {
        if (TryGetCurrentNovel(out var _))
        {
            var names = Plugin.Trans.GetTable("names");
            if (
                !string.IsNullOrEmpty(name)
                && names != null
                && names.TryGetValue(name, out string tName)
            )
                name = tName;
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NovelText), nameof(NovelText.Parse))]
    public static void SetText(List<Letter> letters, ref string message)
    {
        if (TryGetCurrentNovel(out var translation))
        {
            if (
                !string.IsNullOrEmpty(message)
                && translation.TryGetValue(message, out string tMessage)
            )
                message = tMessage;
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NovelModelMessageLog), nameof(NovelModelMessageLog.Add))]
    public static void SetLogAdd(
        string scriptId,
        string assetId,
        ref string charaName,
        ref string message,
        string logId,
        NovelSound voice,
        CancellationToken ct
    )
    {
        charaName = charaName?.Replace("<user>", "%user%");
        message = message?.Replace("<user>", "%user%");
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NovelLogPopup), nameof(NovelLogPopup.SetData))]
    public static void SetLog(ref List<NovelLogData> dataList)
    {
        List<NovelLogData> list = new();
        foreach (var data in dataList)
        {
            string name = data.Name?.Replace("%user%", "<user>");
            string message = data.Message?.Replace("%user%", "<user>");

            if (TryGetCurrentNovel(out var translation))
            {
                var names = Plugin.Trans.GetTable("names");
                if (
                    !string.IsNullOrEmpty(name)
                    && names != null
                    && names.TryGetValue(name, out string tName)
                )
                    name = tName;

                if (
                    !string.IsNullOrEmpty(message)
                    && translation.TryGetValue(message, out string tMessage)
                )
                    message = tMessage;
            }

            list.Add(
                new NovelLogData(
                    data.ScriptId,
                    data.AssetId,
                    name,
                    message,
                    data.LogId,
                    data.Voice,
                    data.Ct
                )
            );
        }
        dataList = list;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NovelModelDotBalloon), nameof(NovelModelDotBalloon.StartBalloonMessage))]
    public static void SetBalloon(CommandDotMessageData messageData)
    {
        if (TryGetCurrentNovel(out var translation))
        {
            string message = messageData.Message;
            if (
                !string.IsNullOrEmpty(message)
                && translation.TryGetValue(message, out string tMessage)
            )
                messageData.Message = tMessage;
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(
        typeof(NovelMessageTextComponent),
        nameof(NovelMessageTextComponent.SetMessageText)
    )]
    public static void SetTextCenter(NovelModelCommon common, CommandMessageTextData data)
    {
        if (TryGetCurrentNovel(out var translation))
        {
            string message = data.Message;
            if (
                !string.IsNullOrEmpty(message)
                && translation.TryGetValue(message, out string tMessage)
            )
                data.Message = tMessage;
        }
    }

    // Codex-added TMP UI translation: dynamic assignments pass through set_text.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(TMP_Text), "set_text")]
    public static void SetUiText(TMP_Text __instance, ref string value)
    {
        try
        {
            value = TranslateUiText(__instance, value);
        }
        catch (System.Exception e)
        {
            DisableUiTextTranslationAfterError(e);
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(TMP_Text), nameof(TMP_Text.SetText), typeof(string))]
    public static void SetUiTextBySetText(TMP_Text __instance, ref string sourceText)
    {
        try
        {
            sourceText = TranslateUiText(__instance, sourceText);
        }
        catch (System.Exception e)
        {
            DisableUiTextTranslationAfterError(e);
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(TMP_Text), nameof(TMP_Text.SetText), typeof(string), typeof(bool))]
    public static bool SetUiTextBySetTextSync(
        TMP_Text __instance,
        ref string sourceText,
        bool syncTextInputBox
    )
    {
        try
        {
            __instance.text = TranslateUiText(__instance, sourceText);
        }
        catch (System.Exception e)
        {
            DisableUiTextTranslationAfterError(e);
        }

        return false;
    }

    // Codex-added TMP UI translation: prefab/static texts often only exist after OnEnable.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(TextMeshProUGUI), "OnEnable")]
    public static void TranslateStaticUiText(TextMeshProUGUI __instance)
    {
        TranslateStaticUiText((TMP_Text)__instance);
    }

    // Codex-added TMP UI translation: covers 3D/world TextMeshPro as well as UGUI.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(TextMeshPro), "OnEnable")]
    public static void TranslateStaticUiText(TextMeshPro __instance)
    {
        TranslateStaticUiText((TMP_Text)__instance);
    }

    private static void TranslateStaticUiText(TMP_Text text)
    {
        if (text == null)
            return;

        try
        {
            string translated = TranslateUiText(text, text.text);
            if (!string.Equals(translated, text.text, System.StringComparison.Ordinal))
                text.text = translated;
        }
        catch (System.Exception e)
        {
            DisableUiTextTranslationAfterError(e);
        }
    }

    private static void DisableUiTextTranslationAfterError(System.Exception e)
    {
        if (_uiTextErrorLogged)
            return;

        _uiTextErrorLogged = true;
        Logger.Warn($"UI text translation disabled after error: {e.Message}");
    }
}

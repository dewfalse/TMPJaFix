using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using TMPro;
using UnityEngine;
using System;
using System.IO;
using Il2CppDict = Il2CppSystem.Collections.Generic.Dictionary<int, char>;
using Il2CppList = Il2CppSystem.Collections.Generic.List<TMPro.TMP_FontAsset>;

[BepInPlugin("com.user.tmpjafix", "TMP Japanese Fix", "1.0.0")]
public class Plugin : BasePlugin
{
    public static BepInEx.Logging.ManualLogSource? Logger;

    public override void Load()
    {
        Logger = Log;

        var cfgBundle = Config.Bind(
            "Font",
            "BundleName",
            "arialuni_sdf_u2019",
            "ゲームフォルダに置いたフォントバンドルのファイル名（拡張子なし）。\n" +
            "例: arialuni_sdf_u2019  /  noto_sans_jp");

        var cfgScale = Config.Bind(
            "Font",
            "JapaneseFontSizeScale",
            0.8f,
            "日本語テキストのフォントサイズ倍率。1.0 = 等倍、0.8 = 80%。");

        TMP_Patch.FontBundleName = cfgBundle.Value;
        TMP_Patch.FontSizeScale  = cfgScale.Value;

        Harmony.CreateAndPatchAll(typeof(TMP_Patch));
        Log.LogInfo($"TMP Japanese Fix: Loaded! " +
                    $"(bundle={TMP_Patch.FontBundleName}, scale={TMP_Patch.FontSizeScale})");
    }
}

[HarmonyPatch]
static class TMP_Patch
{
    // ── 設定値（Plugin.Load() から注入）──────────────────────────────────────
    public static string FontBundleName = "arialuni_sdf_u2019";
    public static float  FontSizeScale  = 0.8f;

    // ── Patch 1 ────────────────────────────────────────────────────────────────
    // GetCharacters(null) NRE の根本修正
    [HarmonyPatch(typeof(TMP_Settings), "GetCharacters")]
    [HarmonyPrefix]
    static bool GetCharacters_Prefix(TextAsset file, ref Il2CppDict __result)
    {
        if (file == null)
        {
            __result = new Il2CppDict();
            return false;
        }
        return true;
    }

    // ── 共通フォント注入ユーティリティ ──────────────────────────────────────
    static TMP_FontAsset? _jaFont = null;
    static bool _bundleLoadAttempted = false;

    // ── フォントサイズ調整テーブル（インスタンスPtr → オリジナルサイズ）──────
    static readonly System.Collections.Generic.Dictionary<IntPtr, float> _origSize
        = new System.Collections.Generic.Dictionary<IntPtr, float>();

    static void EnsureFontLoaded()
    {
        if (_bundleLoadAttempted) return;
        _bundleLoadAttempted = true;
        TryLoadJapaneseFont();
    }

    static void InjectFallback(TMP_FontAsset? font)
    {
        if (_jaFont == null || font == null) return;
        if (font.Pointer == _jaFont.Pointer) return; // 日本語フォント自身はスキップ
        try
        {
            var list = font.fallbackFontAssetTable;
            if (list == null)
            {
                list = new Il2CppList();
                font.fallbackFontAssetTable = list;
            }
            if (!list.Contains(_jaFont))
            {
                list.Add(_jaFont);
                Plugin.Logger?.LogInfo($"[TMPJaFix] Injected ja-font into: {font.name}");
            }
        }
        catch (Exception e)
        {
            Plugin.Logger?.LogWarning($"[TMPJaFix] Inject failed on '{font?.name}': {e.Message}");
        }
    }

    static void TryLoadJapaneseFont()
    {
        try
        {
            string path = Path.Combine(BepInEx.Paths.GameRootPath, FontBundleName);
            if (!File.Exists(path))
            {
                Plugin.Logger?.LogWarning($"[TMPJaFix] Font bundle not found at: {path}");
                return;
            }
            var bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null)
            {
                Plugin.Logger?.LogWarning("[TMPJaFix] AssetBundle.LoadFromFile returned null.");
                return;
            }
            foreach (var obj in bundle.LoadAllAssets())
            {
                var fa = obj?.TryCast<TMP_FontAsset>();
                if (fa != null)
                {
                    _jaFont = fa;
                    Plugin.Logger?.LogInfo($"[TMPJaFix] Japanese font loaded: {fa.name}");
                    break;
                }
            }
            if (_jaFont == null)
                Plugin.Logger?.LogWarning("[TMPJaFix] No TMP_FontAsset found inside the bundle.");
        }
        catch (Exception e)
        {
            Plugin.Logger?.LogWarning($"[TMPJaFix] Exception while loading font bundle: {e.Message}");
        }
    }

    // ── Patch 2 ────────────────────────────────────────────────────────────────
    // TMP_FontAsset.Awake: ゲームプレイ中に新規ロードされたフォント向け
    [HarmonyPatch(typeof(TMP_FontAsset), "Awake")]
    [HarmonyPostfix]
    static void FontAsset_Awake_Postfix(TMP_FontAsset __instance)
    {
        EnsureFontLoaded();
        InjectFallback(__instance);
    }

    // ── Patch 3 ────────────────────────────────────────────────────────────────
    // TextMeshProUGUI.Awake: UI テキスト（メニュー等）
    [HarmonyPatch(typeof(TextMeshProUGUI), "Awake")]
    [HarmonyPostfix]
    static void TMPUGUI_Awake_Postfix(TextMeshProUGUI __instance)
    {
        EnsureFontLoaded();
        InjectFallback(__instance?.font);
        if (__instance != null) _origSize.Remove(__instance.Pointer);
    }

    // ── Patch 4 ────────────────────────────────────────────────────────────────
    // TextMeshPro.Awake: ワールド空間テキスト向け
    [HarmonyPatch(typeof(TextMeshPro), "Awake")]
    [HarmonyPostfix]
    static void TMP_Awake_Postfix(TextMeshPro __instance)
    {
        EnsureFontLoaded();
        InjectFallback(__instance?.font);
        if (__instance != null) _origSize.Remove(__instance.Pointer);
    }

    // ── Patch 5a / 5b ─────────────────────────────────────────────────────────
    // TextMeshProUGUI.Rebuild / TextMeshPro.Rebuild Prefix:
    // メッシュ再構築直前（string パラメータなし → IL2CPP 安全）に
    // 日本語テキストを検出してフォントサイズを FontSizeScale 倍に縮小する。
    [HarmonyPatch(typeof(TextMeshProUGUI), "Rebuild")]
    [HarmonyPrefix]
    static void TMPUGUI_Rebuild_Prefix(TextMeshProUGUI __instance)
        => AdjustFontSizeForJa(__instance);

    [HarmonyPatch(typeof(TextMeshPro), "Rebuild")]
    [HarmonyPrefix]
    static void TMP_Rebuild_Prefix(TextMeshPro __instance)
        => AdjustFontSizeForJa(__instance);

    static void AdjustFontSizeForJa(TMP_Text? instance)
    {
        try
        {
            if (instance == null) return;
            string? text;
            try { text = instance.text; } catch { return; }
            if (string.IsNullOrEmpty(text)) return;

            var ptr = instance.Pointer;
            bool hasJa = ContainsJapanese(text);
            bool autoSize = instance.enableAutoSizing;

            if (hasJa)
            {
                if (!_origSize.ContainsKey(ptr))
                    _origSize[ptr] = autoSize ? instance.fontSizeMax : instance.fontSize;

                float target = _origSize[ptr] * FontSizeScale;
                if (autoSize)
                { if (Math.Abs(instance.fontSizeMax - target) > 0.05f) instance.fontSizeMax = target; }
                else
                { if (Math.Abs(instance.fontSize - target) > 0.05f) instance.fontSize = target; }
            }
            else
            {
                if (_origSize.TryGetValue(ptr, out float orig))
                {
                    if (autoSize)
                    { if (Math.Abs(instance.fontSizeMax - orig) > 0.05f) instance.fontSizeMax = orig; }
                    else
                    { if (Math.Abs(instance.fontSize - orig) > 0.05f) instance.fontSize = orig; }
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Logger?.LogWarning($"[TMPJaFix] AdjustFontSizeForJa: {e.Message}");
        }
    }

    static bool ContainsJapanese(string text)
    {
        foreach (char c in text)
        {
            // U+3040-9FFF: ひらがな、カタカナ、CJK統合漢字 等
            if (c >= '぀' && c <= '鿿') return true;
            // U+F900-FAFF: CJK互換漢字
            if (c >= '豈' && c <= '﫿') return true;
        }
        return false;
    }

}

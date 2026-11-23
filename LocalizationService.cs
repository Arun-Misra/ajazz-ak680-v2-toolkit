using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AK680V2Toolkit;

/// <summary>
/// Runtime UI translation. Walks the visual tree and swaps known English labels for
/// their Chinese/Japanese equivalents. The original English label is remembered in an
/// attached property so switching language back and forth is lossless.
/// </summary>
public static class LocalizationService
{
    public static readonly DependencyProperty SourceTextProperty =
        DependencyProperty.RegisterAttached("SourceText", typeof(string),
            typeof(LocalizationService), new PropertyMetadata(null));

    private static readonly Dictionary<string, string> Zh = new(StringComparer.Ordinal)
    {
        // Navigation / shell
        ["Custom Keys"] = "自定义按键", ["Lighting Settings"] = "灯光设置",
        ["Shortcut Command"] = "快捷指令", ["Performance"] = "性能",
        ["Advanced Keys"] = "高级按键", ["Settings"] = "设置",
        ["Profiles"] = "配置文件", ["Animations"] = "动画",
        ["Hall Gamepad"] = "霍尔手柄", ["Device Inspector"] = "设备检查",
        ["Key Test"] = "按键测试", ["Key Map Debug"] = "键位调试",
        ["Keyboard Config File"] = "键盘配置文件", ["Create File"] = "创建文件",
        ["Import files"] = "导入文件", ["Refresh device"] = "刷新设备",
        ["Not connected"] = "未连接", ["profile"] = "配置文件",
        // Settings
        ["Interface Theme"] = "界面主题", ["Interface Settings"] = "界面设置",
        ["Light Theme"] = "浅色主题", ["Dark Theme"] = "深色主题",
        ["Sync theme with computer"] = "跟随系统主题", ["Language"] = "语言",
        ["Update"] = "更新", ["Check device"] = "检查设备",
        ["Choose firmware…"] = "选择固件…", ["Verify and flash firmware"] = "验证并刷写固件",
        ["No firmware file selected"] = "未选择固件文件", ["Keyboard Settings"] = "键盘设置",
        ["Firmware / Hall probe"] = "固件 / 霍尔探测", ["FW info"] = "固件信息",
        // Common actions
        ["New"] = "新建", ["Delete"] = "删除", ["Import"] = "导入", ["Export"] = "导出",
        ["Rename"] = "重命名", ["Copy"] = "复制", ["Create"] = "创建", ["Cancel"] = "取消",
        ["Reset"] = "重置", ["Start"] = "开始", ["Stop"] = "停止", ["Play"] = "播放",
        ["Status"] = "状态", ["Overview"] = "概览", ["Configuration"] = "配置",
        ["Save to keyboard"] = "保存到键盘", ["Apply to keyboard"] = "应用到键盘",
        ["Save from keyboard"] = "从键盘保存", ["Apply settings"] = "应用设置",
        ["Apply keyboard settings"] = "应用键盘设置", ["Apply remap"] = "应用重映射",
        ["Apply to selected keys"] = "应用到所选按键", ["Clear all"] = "全部清除",
        ["Select All"] = "全选", ["Remove selected"] = "移除所选",
        // Lighting / animation
        ["Lighting Effects"] = "灯光效果", ["Lighting Edit"] = "灯光编辑",
        ["Color settings"] = "颜色设置", ["Lighting Brightness"] = "灯光亮度",
        ["Speed"] = "速度", ["Animation speed"] = "动画速度", ["Direction"] = "方向",
        ["Primary colour"] = "主色", ["Secondary colour"] = "次色",
        ["Rainbow"] = "彩虹", ["Breathing"] = "呼吸", ["Wave"] = "波浪",
        ["Ripple"] = "涟漪", ["Heart"] = "心形", ["Chase"] = "追逐",
        ["Sparkle"] = "闪烁", ["Pulse"] = "脉冲", ["Aurora"] = "极光",
        ["Meteor"] = "流星", ["Comet"] = "彗星", ["Nova"] = "新星",
        ["Prism Bloom"] = "棱镜绽放", ["Liquid Bloom"] = "流光绽放",
        ["Matrix Rain"] = "矩阵雨", ["KITT Scanner"] = "KITT 扫描",
        ["Display Mode"] = "显示模式", ["Amplitude"] = "振幅",
        ["Background Lighting Display Mode"] = "背景灯光显示模式", ["Background Brightness"] = "背景亮度",
        ["Apply firmware effect"] = "应用固件音效", ["Plugin status: connected — firmware effects ready"] = "插件状态：已连接",
        ["Custom Animations"] = "自定义动画", ["Music Rhythm"] = "音乐节奏",
        ["Musical Rhythm"] = "音乐节奏", ["Typing Trail"] = "打字轨迹",
        // Performance
        ["Stability Mode"] = "稳定模式", ["Stability mode"] = "稳定模式",
        ["Adaptive Dynamic Calibration (Beta)"] = "自适应动态校准（测试版）",
        ["Adaptive Dynamic Calibration"] = "自适应动态校准",
        ["Enable adaptive calibration"] = "启用自适应校准",
        ["Enable stability mode"] = "启用稳定模式",
        ["Return rate"] = "回报率", ["Return Rate"] = "回报率", ["Trigger Distance"] = "触发行程",
        ["Dead Zone"] = "死区", ["Top"] = "上", ["Bottom"] = "下",
        ["Fast Trigger"] = "快速触发", ["Calibrate full travel"] = "校准全行程",
        ["Switch Calibration"] = "开关校准", ["Game Mode"] = "游戏模式",
        ["Office Mode"] = "办公模式", ["Beginner Mode"] = "新手模式",
        ["Trigger & RT"] = "触发与快速触发", ["Keys & LEDs"] = "按键与灯光",
        ["DKS & macros"] = "DKS 与宏",
        // Advanced keys
        ["Advanced Settings"] = "高级设置",
        ["MT - tap / hold (1 key)"] = "MT - 轻触/长按（1键）",
        ["TGL - toggle (1 key)"] = "TGL - 切换（1键）",
        ["RS - Rappy Snappy (2 keys)"] = "RS - 快速响应（2键）",
        ["SOCD - priority (2 keys)"] = "SOCD - 优先级（2键）",
        ["DKS - dynamic travel (1 key)"] = "DKS - 动态行程（1键）",
        // Macro / profiles / misc
        ["Input Record List"] = "输入录制列表", ["Action Record List"] = "动作录制列表",
        ["Edit recording file"] = "编辑录制文件", ["Action type"] = "动作类型",
        ["Key code (0-255)"] = "键码（0-255）", ["Delay (ms)"] = "延迟（毫秒）",
        ["Keyboard Input Action"] = "键盘输入动作", ["Mouse Input Action"] = "鼠标输入动作",
        ["Record from physical keys"] = "从实体按键录制", ["Start recording"] = "开始录制",
        ["Insert action"] = "插入动作", ["Record interval"] = "记录间隔",
        ["Onboard slot"] = "板载槽位", ["Apply"] = "应用",
        ["Key travel (0-3.4 mm)"] = "按键行程（0-3.4 毫米）",
        ["Last clicked"] = "最后按下", ["Live analog"] = "实时模拟量",
        ["Start monitoring"] = "开始监控", ["Hall WASD"] = "霍尔 WASD",
        ["WASD"] = "WASD", ["Read state"] = "读取状态",
        ["Read everything"] = "读取全部", ["Raw replies"] = "原始回复",
        ["Save report"] = "保存报告", ["No data yet."] = "暂无数据。",
        ["Selected key: -"] = "所选按键：-", ["No keys selected"] = "未选择按键",
        ["Reset all keyboard settings"] = "重置所有键盘设置",
        ["Device system info not read yet."] = "尚未读取设备系统信息。",
        ["Enabling stability mode optimizes the key algorithm under extreme parameters."] =
            "启用稳定模式可优化极端参数下的按键算法。",
    };

    private static readonly Dictionary<string, string> Ja = new(StringComparer.Ordinal)
    {
        // Navigation / shell
        ["Custom Keys"] = "カスタムキー", ["Lighting Settings"] = "ライティング設定",
        ["Shortcut Command"] = "ショートカットコマンド", ["Performance"] = "パフォーマンス",
        ["Advanced Keys"] = "高度なキー", ["Settings"] = "設定",
        ["Profiles"] = "プロファイル", ["Animations"] = "アニメーション",
        ["Hall Gamepad"] = "Hallゲームパッド", ["Device Inspector"] = "デバイスインスペクター",
        ["Key Test"] = "キーテスト", ["Key Map Debug"] = "キーマップデバッグ",
        ["Keyboard Config File"] = "キーボード設定ファイル", ["Create File"] = "ファイルを作成",
        ["Import files"] = "ファイルをインポート", ["Refresh device"] = "デバイスを更新",
        ["Not connected"] = "未接続", ["profile"] = "プロファイル",
        // Settings
        ["Interface Theme"] = "インターフェースのテーマ", ["Interface Settings"] = "インターフェース設定",
        ["Light Theme"] = "ライトテーマ", ["Dark Theme"] = "ダークテーマ",
        ["Sync theme with computer"] = "システムのテーマに同期", ["Language"] = "言語",
        ["Update"] = "アップデート", ["Check device"] = "デバイスを確認",
        ["Choose firmware…"] = "ファームウェアを選択…", ["Verify and flash firmware"] = "ファームウェアを検証して書き込み",
        ["No firmware file selected"] = "ファームウェアファイルが選択されていません", ["Keyboard Settings"] = "キーボード設定",
        ["Firmware / Hall probe"] = "ファームウェア / Hall探针", ["FW info"] = "ファームウェア情報",
        // Common actions
        ["New"] = "新規", ["Delete"] = "削除", ["Import"] = "インポート", ["Export"] = "エクスポート",
        ["Rename"] = "名前を変更", ["Copy"] = "コピー", ["Create"] = "作成", ["Cancel"] = "キャンセル",
        ["Reset"] = "リセット", ["Start"] = "開始", ["Stop"] = "停止", ["Play"] = "再生",
        ["Status"] = "ステータス", ["Overview"] = "概要", ["Configuration"] = "構成",
        ["Save to keyboard"] = "キーボードに保存", ["Apply to keyboard"] = "キーボードに適用",
        ["Save from keyboard"] = "キーボードから保存", ["Apply settings"] = "設定を適用",
        ["Apply keyboard settings"] = "キーボード設定を適用", ["Apply remap"] = "リマップを適用",
        ["Apply to selected keys"] = "選択したキーに適用", ["Clear all"] = "すべてクリア",
        ["Select All"] = "すべて選択", ["Remove selected"] = "選択を削除",
        // Lighting / animation
        ["Lighting Effects"] = "ライティング効果", ["Lighting Edit"] = "ライティング編集",
        ["Color settings"] = "カラー設定", ["Lighting Brightness"] = "ライティングの明るさ",
        ["Speed"] = "速度", ["Animation speed"] = "アニメーション速度", ["Direction"] = "方向",
        ["Primary colour"] = "プライマリカラー", ["Secondary colour"] = "セカンダリカラー",
        ["Rainbow"] = "レインボー", ["Breathing"] = "ブリージング", ["Wave"] = "ウェーブ",
        ["Ripple"] = "リップル", ["Heart"] = "ハート", ["Chase"] = "チェイス",
        ["Sparkle"] = "スパークル", ["Pulse"] = "パルス", ["Aurora"] = "オーロラ",
        ["Meteor"] = "流星", ["Comet"] = "彗星", ["Nova"] = "ノヴァ",
        ["Prism Bloom"] = "プリズムブルーム", ["Liquid Bloom"] = "リキッドブルーム",
        ["Matrix Rain"] = "マトリックスレイン", ["KITT Scanner"] = "KITTスキャナー",
        ["Display Mode"] = "表示モード", ["Amplitude"] = "振幅",
        ["Background Lighting Display Mode"] = "背景ライティング表示モード", ["Background Brightness"] = "背景の明るさ",
        ["Apply firmware effect"] = "ファームウェア効果を適用", ["Plugin status: connected — firmware effects ready"] = "プラグイン状態：接続済み",
        ["Custom Animations"] = "カスタムアニメーション", ["Music Rhythm"] = "ミュージックリズム",
        ["Musical Rhythm"] = "ミュージックリズム", ["Typing Trail"] = "タイピングトレイル",
        // Performance
        ["Stability Mode"] = "安定モード", ["Stability mode"] = "安定モード",
        ["Adaptive Dynamic Calibration (Beta)"] = "適応動的キャリブレーション（ベータ）",
        ["Adaptive Dynamic Calibration"] = "適応動的キャリブレーション",
        ["Enable adaptive calibration"] = "適応キャリブレーションを有効にする",
        ["Enable stability mode"] = "安定モードを有効にする",
        ["Return rate"] = "レポートレート", ["Return Rate"] = "レポートレート", ["Trigger Distance"] = "トリガー距離",
        ["Dead Zone"] = "デッドゾーン", ["Top"] = "上", ["Bottom"] = "下",
        ["Fast Trigger"] = "クイックトリガー", ["Calibrate full travel"] = "全ストロークを較正",
        ["Switch Calibration"] = "スイッチ較正", ["Game Mode"] = "ゲームモード",
        ["Office Mode"] = "オフィスモード", ["Beginner Mode"] = "初心者モード",
        ["Trigger & RT"] = "トリガーとRT", ["Keys & LEDs"] = "キーとLED",
        ["DKS & macros"] = "DKSとマクロ",
        // Advanced keys
        ["Advanced Settings"] = "詳細設定",
        ["MT - tap / hold (1 key)"] = "MT - タップ/ホールド（1キー）",
        ["TGL - toggle (1 key)"] = "TGL - トグル（1キー）",
        ["RS - Rappy Snappy (2 keys)"] = "RS - ラピッドトリガー（2キー）",
        ["SOCD - priority (2 keys)"] = "SOCD - 優先度（2キー）",
        ["DKS - dynamic travel (1 key)"] = "DKS - 動的ストローク（1キー）",
        // Macro / profiles / misc
        ["Input Record List"] = "入力記録リスト", ["Action Record List"] = "動作記録リスト",
        ["Edit recording file"] = "記録ファイルを編集", ["Action type"] = "動作タイプ",
        ["Key code (0-255)"] = "キーコード（0-255）", ["Delay (ms)"] = "遅延（ms）",
        ["Keyboard Input Action"] = "キーボード入力動作", ["Mouse Input Action"] = "マウス入力動作",
        ["Record from physical keys"] = "物理キーから録画", ["Start recording"] = "録画を開始",
        ["Insert action"] = "動作を挿入", ["Record interval"] = "間隔を記録",
        ["Onboard slot"] = "オンボードスロット", ["Apply"] = "適用",
        ["Key travel (0-3.4 mm)"] = "キーストローク（0-3.4 mm）",
        ["Last clicked"] = "最後の押下", ["Live analog"] = "リアルタイムアナログ",
        ["Start monitoring"] = "監視を開始", ["Hall WASD"] = "Hall WASD",
        ["WASD"] = "WASD", ["Read state"] = "状態を読み取る",
        ["Read everything"] = "すべて読み取る", ["Raw replies"] = "生の応答",
        ["Save report"] = "レポートを保存", ["No data yet."] = "データがありません。",
        ["Selected key: -"] = "選択中のキー：-", ["No keys selected"] = "キーが選択されていません",
        ["Reset all keyboard settings"] = "すべてのキーボード設定をリセット",
        ["Device system info not read yet."] = "デバイスのシステム情報はまだ読み取られていません。",
        ["Enabling stability mode optimizes the key algorithm under extreme parameters."] =
            "安定モードを有効にすると、極端なパラメータ下でのキーアルゴリズムが最適化されます。",
    };

    public static string Translate(string text, string language)
    {
        if (string.IsNullOrEmpty(text) || language == "English") return text;
        var dict = language == "中文" ? Zh : language == "日本語" ? Ja : null;
        return dict != null && dict.TryGetValue(text, out var value) ? value : text;
    }

    /// <summary>Re-translate every currently visible window (including the active page in the frame).</summary>
    public static void Refresh()
    {
        if (Application.Current == null) return;
        foreach (Window window in Application.Current.Windows) Apply(window);
    }

    public static void Apply(FrameworkElement root) => ApplyTo(root, ThemeService.Language);

    private static void ApplyTo(DependencyObject element, string language)
    {
        // Dynamic status labels are updated in code-behind; never translate them.
        bool skip = element is FrameworkElement fe && fe.Name.Contains("Status", StringComparison.OrdinalIgnoreCase);

        switch (element)
        {
            case TextBlock tb when !skip && tb.Parent is not ContentPresenter:
                SetText(tb, TextBlock.TextProperty, tb.Text, language);
                break;
            case Label lab when !skip && lab.Content is string ls:
                SetText(lab, Label.ContentProperty, ls, language);
                break;
            case ContentControl cc when !skip && cc.Content is string cs:
                // Covers Button, CheckBox, RadioButton, ListBoxItem, etc.
                SetText(cc, ContentControl.ContentProperty, cs, language);
                break;
            case TabItem ti when !skip && ti.Header is string hs:
                SetText(ti, TabItem.HeaderProperty, hs, language);
                break;
            case DataGrid grid:
                foreach (var col in grid.Columns)
                    if (col.Header is string ch) SetText(col, DataGridColumn.HeaderProperty, ch, language);
                break;
        }

        int count = VisualTreeHelper.GetChildrenCount(element);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            if (child is Visual) ApplyTo(child, language);
        }
    }

    private static void SetText(DependencyObject element, DependencyProperty property,
        string current, string language)
    {
        string original = element.GetValue(SourceTextProperty) as string ?? current;
        element.SetValue(SourceTextProperty, original);
        var translated = Translate(original, language);
        if (translated != current && !Equals(element.GetValue(property), translated))
            element.SetValue(property, translated);
    }
}

using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using BetterMultiplayer.Diagnostics;
using BetterMultiplayer.Localization;

namespace BetterMultiplayer.UI;

internal static class UiFactory
{
    private const string OfficialPaperButtonBackgroundName = "BetterMultiplayerOfficialPaper";

    /// <summary>
    /// overlay 外层安全区的左右边距。这些数字参与交易界面的宽度预算，
    /// 改之前先看 <see cref="Trading.TradeLayout"/> 与 TradeLayoutTests。
    /// </summary>
    internal const int OverlaySafeAreaMargin = 24;

    /// <summary>overlay 内容区的左右边距。同样参与宽度预算。</summary>
    internal const int OverlayContentMargin = 40;

    internal static readonly Color Background = new("111416");
    internal static readonly Color Surface = new("202529");
    internal static readonly Color Border = new("485159");
    internal static readonly Color Accent = new("d6a84b");
    internal static readonly Color Good = new("58a66a");
    internal static readonly Color Danger = new("b65a55");
    internal static readonly Color TextMuted = new("aeb7bc");

    internal static Control CreateOverlay(
        string title,
        Vector2 minimumSize,
        Action onClose,
        out VBoxContainer body,
        out Label status)
    {
        Control root = new()
        {
            Name = "BetterMultiplayerOverlay",
            MouseFilter = Control.MouseFilterEnum.Stop,
            ZIndex = 4000
        };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        ColorRect shade = new()
        {
            Color = new Color(0f, 0f, 0f, 0.78f),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(shade);

        CenterContainer center = new();
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(center);

        PanelContainer panel = new() { CustomMinimumSize = minimumSize };
        panel.AddThemeStyleboxOverride("panel", PanelStyle(Surface, Border, 2, 6));
        center.AddChild(panel);

        MarginContainer margin = new();
        margin.AddThemeConstantOverride("margin_left", 28);
        margin.AddThemeConstantOverride("margin_right", 28);
        margin.AddThemeConstantOverride("margin_top", 22);
        margin.AddThemeConstantOverride("margin_bottom", 22);
        panel.AddChild(margin);

        VBoxContainer frame = new();
        frame.AddThemeConstantOverride("separation", 16);
        margin.AddChild(frame);

        HBoxContainer header = new();
        frame.AddChild(header);

        Label heading = Label(title, 28);
        heading.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(heading);

        Button close = Button(ModText.Get(TextKey.Close), onClose);
        close.TooltipText = ModText.Get(TextKey.Close);
        header.AddChild(close);

        HSeparator separator = new();
        separator.AddThemeConstantOverride("separation", 1);
        frame.AddChild(separator);

        body = new VBoxContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        body.AddThemeConstantOverride("separation", 12);
        frame.AddChild(body);

        status = Label(string.Empty, 16, TextMuted);
        status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        status.CustomMinimumSize = new Vector2(0, 38);
        frame.AddChild(status);

        return root;
    }

    internal static Control CreateBlurredOverlay(out VBoxContainer body, out Label status)
    {
        Control root = new()
        {
            Name = "BetterMultiplayerOverlay",
            MouseFilter = Control.MouseFilterEnum.Stop,
            ZIndex = 4000
        };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        root.AddChild(CreateBlurredBackdrop());

        MarginContainer margin = new();
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 72);
        margin.AddThemeConstantOverride("margin_right", 72);
        margin.AddThemeConstantOverride("margin_top", 48);
        margin.AddThemeConstantOverride("margin_bottom", 34);
        root.AddChild(margin);

        VBoxContainer frame = new();
        frame.AddThemeConstantOverride("separation", 12);
        margin.AddChild(frame);

        body = new VBoxContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        body.AddThemeConstantOverride("separation", 12);
        frame.AddChild(body);

        status = Label(string.Empty, 16, TextMuted);
        status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        status.HorizontalAlignment = HorizontalAlignment.Center;
        status.CustomMinimumSize = new Vector2(0, 38);
        frame.AddChild(status);

        return root;
    }

    internal static Control CreateTexturedOverlay(
        string title,
        Action onBack,
        out VBoxContainer body,
        out Label status,
        out NBackButton backButton)
    {
        Control root = new()
        {
            Name = "BetterMultiplayerTexturedOverlay",
            MouseFilter = Control.MouseFilterEnum.Stop,
            ZIndex = 4000
        };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(CreateBlurredBackdrop());

        MarginContainer safeArea = new();
        safeArea.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        safeArea.AddThemeConstantOverride("margin_left", OverlaySafeAreaMargin);
        safeArea.AddThemeConstantOverride("margin_right", OverlaySafeAreaMargin);
        safeArea.AddThemeConstantOverride("margin_top", 20);
        safeArea.AddThemeConstantOverride("margin_bottom", 20);
        root.AddChild(safeArea);

        Control paper = new()
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        safeArea.AddChild(paper);

        Control background = CreateOfficialPaperBackground();
        background.MouseFilter = Control.MouseFilterEnum.Ignore;
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        paper.AddChild(background);

        MarginContainer contentMargin = new();
        contentMargin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        contentMargin.AddThemeConstantOverride("margin_left", OverlayContentMargin);
        contentMargin.AddThemeConstantOverride("margin_right", OverlayContentMargin);
        contentMargin.AddThemeConstantOverride("margin_top", 36);
        contentMargin.AddThemeConstantOverride("margin_bottom", 30);
        paper.AddChild(contentMargin);

        VBoxContainer frame = new();
        frame.AddThemeConstantOverride("separation", 12);
        contentMargin.AddChild(frame);

        Label heading = Label(title, 32, Accent);
        heading.HorizontalAlignment = HorizontalAlignment.Center;
        heading.CustomMinimumSize = new Vector2(0, 48);
        frame.AddChild(heading);

        body = new VBoxContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        body.AddThemeConstantOverride("separation", 12);
        frame.AddChild(body);

        status = Label(string.Empty, 18, TextMuted);
        status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        status.HorizontalAlignment = HorizontalAlignment.Center;
        status.CustomMinimumSize = new Vector2(0, 42);
        frame.AddChild(status);

        backButton = CreateOfficialBackButton(onBack);
        root.AddChild(backButton);
        return root;
    }

    internal static NBackButton CreateOfficialBackButton(Action onReleased)
    {
        PackedScene? scene = GD.Load<PackedScene>("res://scenes/ui/back_button.tscn");
        if (scene is null)
            throw new InvalidOperationException("Could not load the official back button resource.");

        NBackButton backButton = scene.Instantiate<NBackButton>();
        backButton.Name = "BetterMultiplayerBackButton";
        backButton.Connect(
            NClickableControl.SignalName.Released,
            Callable.From<NButton>(_ => onReleased()));
        backButton.Ready += backButton.Enable;
        return backButton;
    }

    internal static Control CreateOfficialPaperBackground()
    {
        Texture2D? texture = GD.Load<Texture2D>(
            "res://images/atlases/ui_atlas.sprites/popup_vertical.tres");
        Shader? shader = GD.Load<Shader>("res://shaders/hsv.gdshader");
        if (texture is null || shader is null)
        {
            PanelContainer fallback = new();
            fallback.AddThemeStyleboxOverride(
                "panel",
                PanelStyle(new Color("263d4a"), new Color("162a35"), 3, 8));
            return fallback;
        }

        ShaderMaterial material = new() { Shader = shader };
        material.SetShaderParameter("h", 0.505f);
        material.SetShaderParameter("s", 1.0f);
        material.SetShaderParameter("v", 0.75f);

        return new NinePatchRect
        {
            Texture = texture,
            Material = material,
            DrawCenter = true,
            PatchMarginLeft = 76,
            PatchMarginTop = 82,
            PatchMarginRight = 76,
            PatchMarginBottom = 82,
            AxisStretchHorizontal = NinePatchRect.AxisStretchMode.Stretch,
            AxisStretchVertical = NinePatchRect.AxisStretchMode.Stretch
        };
    }

    private static ColorRect CreateBlurredBackdrop()
    {
        ColorRect backdrop = new()
        {
            Color = Colors.White,
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        Shader? blurShader = GD.Load<Shader>("res://shaders/dark_blur.gdshader");
        if (blurShader is not null)
        {
            ShaderMaterial blurMaterial = new() { Shader = blurShader };
            blurMaterial.SetShaderParameter("lod", 5.0f);
            blurMaterial.SetShaderParameter("mix_percentage", 0.3f);
            backdrop.Material = blurMaterial;
        }
        else
        {
            backdrop.Color = new Color(0f, 0f, 0f, 0.72f);
        }
        backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return backdrop;
    }

    internal static Label Label(string text, int size = 18, Color? color = null)
    {
        Label label;
        FontFile? font = GD.Load<FontFile>("res://fonts/kreon_regular.ttf");
        if (font is not null)
        {
            MegaLabel mega = new();
            mega.AddThemeFontOverride("font", font);
            mega.AutoSizeEnabled = false;
            mega.SetTextAutoSize(text);
            label = mega;
        }
        else
        {
            label = new Label { Text = text };
        }
        label.VerticalAlignment = VerticalAlignment.Center;
        label.AddThemeFontSizeOverride("font_size", size);
        if (color.HasValue)
            label.AddThemeColorOverride("font_color", color.Value);
        return label;
    }

    internal static void SetText(Label label, string text)
    {
        if (label is MegaLabel mega)
            mega.SetTextAutoSize(text);
        else
            label.Text = text;
    }

    internal static Button Button(
        string text,
        Action onPressed,
        bool primary = false,
        bool danger = false,
        string? diagnosticId = null)
    {
        Button button = new()
        {
            Text = text,
            CustomMinimumSize = new Vector2(112, 44),
            FocusMode = Control.FocusModeEnum.None,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        Color background = danger ? Danger : primary ? Accent : new Color("343c42");
        Color hover = background.Lightened(0.12f);
        button.AddThemeStyleboxOverride("normal", PanelStyle(background, Border, 1, 5));
        button.AddThemeStyleboxOverride("hover", PanelStyle(hover, Accent, 1, 5));
        button.AddThemeStyleboxOverride("pressed", PanelStyle(background.Darkened(0.1f), Accent, 2, 5));
        button.AddThemeColorOverride("font_color", primary ? Colors.Black : Colors.White);
        AttachNativeInput(button, onPressed, diagnosticId);
        return button;
    }

    internal static Button OfficialPaperButton(
        string text,
        Action onPressed,
        string? diagnosticId = null)
    {
        Button button = new()
        {
            Text = text,
            CustomMinimumSize = new Vector2(360, 176),
            FocusMode = Control.FocusModeEnum.None,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        StyleBoxEmpty empty = new();
        button.AddThemeStyleboxOverride("normal", empty);
        button.AddThemeStyleboxOverride("hover", empty);
        button.AddThemeStyleboxOverride("pressed", empty);
        button.AddThemeStyleboxOverride("disabled", empty);
        button.AddThemeStyleboxOverride("focus", empty);
        button.AddThemeColorOverride("font_color", Colors.White);
        button.AddThemeColorOverride("font_hover_color", Colors.White);
        button.AddThemeColorOverride("font_pressed_color", Colors.White);
        button.AddThemeColorOverride("font_disabled_color", new Color(0.72f, 0.72f, 0.72f));
        button.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.78f));
        button.AddThemeConstantOverride("outline_size", 4);
        button.AddThemeFontSizeOverride("font_size", 24);

        Control background = CreateOfficialPaperBackground();
        background.Name = OfficialPaperButtonBackgroundName;
        background.MouseFilter = Control.MouseFilterEnum.Ignore;
        background.ShowBehindParent = true;
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        button.AddChild(background);

        AttachNativeInput(button, onPressed, diagnosticId);
        return button;
    }

    /// <summary>
    /// Routes input through the game's NButton system while leaving the Godot Button
    /// in place as the visual skin. The game-wide input layer does not reliably
    /// deliver clicks to ordinary Godot Buttons when an overlay is active.
    /// </summary>
    internal static void AttachNativeInput(
        Button button,
        Action onReleased,
        string? diagnosticId = null)
    {
        if (button.GetNodeOrNull<NButton>("BetterMultiplayerNativeInput") is not null)
            return;

        NativeInputBinding binding = new(button, onReleased, diagnosticId);
        button.AddChild(binding.Input);
        button.MouseFilter = Control.MouseFilterEnum.Ignore;
        binding.Input.SetEnabled(!button.Disabled);
    }

    internal static void SyncNativeInput(Button button)
    {
        if (button.GetNodeOrNull<NButton>("BetterMultiplayerNativeInput") is { } input)
            input.SetEnabled(!button.Disabled);
        SetOfficialPaperButtonState(
            button,
            button.Disabled ? NativeInputBinding.VisualState.Disabled : NativeInputBinding.VisualState.Normal);
    }

    private static void SetOfficialPaperButtonState(
        Button button,
        NativeInputBinding.VisualState state)
    {
        if (button.GetNodeOrNull<CanvasItem>(OfficialPaperButtonBackgroundName) is not { } background)
            return;

        float brightness = state switch
        {
            NativeInputBinding.VisualState.Hover => 1.08f,
            NativeInputBinding.VisualState.Pressed => 0.86f,
            NativeInputBinding.VisualState.Disabled => 0.62f,
            _ => 1f
        };
        background.SelfModulate = new Color(brightness, brightness, brightness);
    }

    private sealed class NativeInputBinding
    {
        internal enum VisualState
        {
            Normal,
            Hover,
            Pressed,
            Disabled
        }

        private readonly Button _button;
        private readonly string? _diagnosticId;
        private readonly StyleBox? _normalStyle;
        private readonly StyleBox? _hoverStyle;
        private readonly StyleBox? _pressedStyle;
        private readonly StyleBox? _disabledStyle;

        internal NButton Input { get; }

        internal NativeInputBinding(Button button, Action onReleased, string? diagnosticId)
        {
            _button = button;
            _diagnosticId = diagnosticId;
            _normalStyle = button.GetThemeStylebox("normal");
            _hoverStyle = button.GetThemeStylebox("hover");
            _pressedStyle = button.GetThemeStylebox("pressed");
            _disabledStyle = button.GetThemeStylebox("disabled");

            Input = new NButton
            {
                Name = "BetterMultiplayerNativeInput",
                MouseFilter = Control.MouseFilterEnum.Stop,
                FocusMode = Control.FocusModeEnum.All
            };
            Input.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            Input.Focused += OnFocused;
            Input.Unfocused += OnUnfocused;
            Input.MousePressed += OnMousePressed;
            Input.MouseReleased += OnMouseReleased;
            Input.Released += _ =>
            {
                if (!GodotObject.IsInstanceValid(_button) || _button.Disabled)
                    return;

                RecordInput("released");
                BetterMultiplayerMod.Logger.Info(
                    $"Native button released: text=\"{_button.Text}\", name={_button.Name}");
                onReleased();
            };
        }

        private void OnFocused(NClickableControl _)
        {
            if (!GodotObject.IsInstanceValid(_button))
                return;
            RecordInput("focused");
            Input.TooltipText = _button.TooltipText;
            SetVisualState(_button.Disabled ? VisualState.Disabled : VisualState.Hover);
        }

        private void OnUnfocused(NClickableControl _)
        {
            RecordInput("unfocused");
            SetVisualState(_button.Disabled ? VisualState.Disabled : VisualState.Normal);
        }

        private void OnMousePressed(InputEvent _)
        {
            RecordInput("mouse_pressed");
            if (!_button.Disabled)
                SetVisualState(VisualState.Pressed);
        }

        private void OnMouseReleased(InputEvent _)
        {
            RecordInput("mouse_released");
            if (!_button.Disabled)
                SetVisualState(VisualState.Hover);
        }

        private void RecordInput(string stage)
        {
            if (_diagnosticId is null ||
                !GodotObject.IsInstanceValid(_button) ||
                !GodotObject.IsInstanceValid(Input))
                return;

            DiagnosticRecorder.RecordControl(stage, _diagnosticId, _button, Input);
        }

        private void SetVisualState(VisualState state)
        {
            if (!GodotObject.IsInstanceValid(_button))
                return;

            StyleBox? style = state switch
            {
                VisualState.Hover => _hoverStyle ?? _normalStyle,
                VisualState.Pressed => _pressedStyle ?? _normalStyle,
                VisualState.Disabled => _disabledStyle ?? _normalStyle,
                _ => _normalStyle
            };
            if (style is not null)
            {
                string slot = state switch
                {
                    VisualState.Hover => "hover",
                    VisualState.Pressed => "pressed",
                    VisualState.Disabled => "disabled",
                    _ => "normal"
                };
                _button.AddThemeStyleboxOverride(slot, style);
            }
            SetOfficialPaperButtonState(_button, state);
        }
    }

    internal static LineEdit LineEdit(string placeholder, bool secret = false, int maxLength = 64)
    {
        LineEdit input = new()
        {
            PlaceholderText = placeholder,
            Secret = secret,
            MaxLength = maxLength,
            CustomMinimumSize = new Vector2(0, 44),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        input.AddThemeStyleboxOverride("normal", PanelStyle(Background, Border, 1, 4));
        input.AddThemeStyleboxOverride("focus", PanelStyle(Background, Accent, 2, 4));
        return input;
    }

    internal static PanelContainer Band(Color? background = null)
    {
        PanelContainer panel = new();
        panel.AddThemeStyleboxOverride("panel", PanelStyle(background ?? Background, Border, 1, 5));
        return panel;
    }

    internal static StyleBoxFlat PanelStyle(Color background, Color border, int borderWidth, int radius)
    {
        return new StyleBoxFlat
        {
            BgColor = background,
            BorderColor = border,
            BorderWidthLeft = borderWidth,
            BorderWidthTop = borderWidth,
            BorderWidthRight = borderWidth,
            BorderWidthBottom = borderWidth,
            CornerRadiusTopLeft = radius,
            CornerRadiusTopRight = radius,
            CornerRadiusBottomLeft = radius,
            CornerRadiusBottomRight = radius,
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 10,
            ContentMarginBottom = 10
        };
    }

    // ==================================================================
    // 官方九宫格样式
    //
    // PanelStyle 是纯色 + 均匀描边 + 统一圆角的 StyleBoxFlat——看着就是
    // 程序画的（所谓"AI 感"）。官方 UI 用的是带纹理的九宫格：边缘不规则、
    // 有手绘质感。下面从 ui_atlas.sprites 加载官方切片当样式底。
    //
    // ★ 2026-09-18：已从 PCK 解包拿到全部 102 个切片的真实 margin，
    //   不再猜。下面字典里的值来自官方 .tres 文件中的 margin = Rect2(...)。
    //   若传入 marginX/marginY != -1，则覆盖字典值（方便实机微调）。
    // ==================================================================

    /// <summary>
    /// 官方切片的默认九宫格边距 (left, top, right, bottom)。
    /// 数据来自 PCK 内 ui_atlas.sprites/*.tres 的 margin 字段。
    /// </summary>
    private static readonly Dictionary<string, (int L, int T, int R, int B)> OfficialMargins = new()
    {
        // 面板 / 弹出框
        ["popup_vertical"]            = (2,  3,  4,  11),

        // 按钮（popup 系列，游戏内对话框用）
        ["popup_confirm_button"]      = (2,  3,  5,  12),
        ["popup_cancel_button"]       = (2,  22, 4,  49),

        // 按钮（普通系列）
        ["confirm_button"]            = (6,  0,  14, 0),
        ["back_button"]               = (0,  0,  0,  0),
        ["proceed_button"]            = (0,  0,  0,  0),
        ["peek_button"]               = (0,  0,  0,  2),

        // 勾选框
        ["checkbox_ticked"]           = (0,  26, 44, 71),
        ["checkbox_unticked"]         = (0,  0,  0,  0),

        // 标签页
        ["settings_tab_selected"]     = (3,  3,  6,  6),
        ["settings_tab_stroke"]       = (5,  5,  10, 10),

        // 滚动条
        ["scrollbar_train_large"]     = (2,  28, 4,  28),
        ["scrollbar_track_center"]    = (6,  7,  12, 14),
        ["scrollbar_track_edge2"]     = (2,  0,  4,  0),
        ["small_scrollbar_train"]     = (0,  0,  0,  0),
        ["small_scrollbar_track_center"] = (11, 4,  19, 8),
        ["small_scrollbar_track_edge"]   = (0,  0,  0,  0),

        // 顶栏图标
        ["top_bar_gold"]              = (16, 18, 26, 31),
        ["top_bar_heart"]             = (0,  0,  0,  0),
        ["top_bar_deck"]              = (0,  0,  0,  0),
        ["top_bar_floor"]             = (0,  0,  0,  0),
        ["top_bar_map"]               = (0,  0,  0,  0),
        ["top_bar_settings"]          = (0,  0,  0,  0),
        ["top_bar_ascension"]         = (0,  0,  0,  0),
        ["top_bar_char_backdrop"]     = (0,  0,  0,  0),
        ["timer_icon"]                = (0,  0,  0,  0),

        // 卡牌边框 / 装饰（基本无 margin，当普通图用）
        ["card_frame_attack_s"]       = (29, 4,  55, 4),
        ["card_frame_power_s"]        = (0,  0,  0,  0),
        ["card_frame_skill_s"]        = (0,  0,  0,  0),
        ["card_frame_quest_s"]        = (0,  0,  0,  0),
        ["card_frame_ancient_s"]      = (0,  0,  0,  0),
        ["card_banner"]               = (0,  0,  0,  0),
        ["ancient_banner"]            = (0,  0,  0,  0),
        ["card_enchant_s"]            = (1,  23, 2,  25),
        ["card_unplayable_icon"]      = (0,  0,  0,  0),

        // 其他小图标
        ["sort_descending"]           = (0,  0,  0,  0),
        ["compendium"]                = (0,  0,  0,  0),
        ["settings_tiny_left_arrow"]  = (0,  0,  0,  0),
        ["settings_tiny_right_arrow"] = (0,  0,  0,  0),
        ["map_clean_up"]              = (0,  0,  0,  0),
        ["map_eraser_1"]              = (0,  0,  0,  0),
        ["map_eraser_2"]              = (0,  0,  0,  0),
        ["map_legend"]                = (0,  0,  0,  0),
        ["map_pencil_1"]              = (0,  0,  0,  0),
        ["map_pencil_2"]              = (0,  0,  0,  0),
    };

    /// <summary>
    /// 从官方 UI 图集加载一个切片，做成九宫格样式。
    /// 若 <paramref name="marginX"/> 或 <paramref name="marginY"/> 为 -1，
    /// 则使用字典中该切片的默认 margin；否则用传入值覆盖。
    /// 加载失败返回 null，调用方回落到 <see cref="PanelStyle"/>。
    /// </summary>
    internal static StyleBoxTexture? OfficialSliceStyle(
        string sliceName,
        Color? modulate = null,
        int marginX = -1,
        int marginY = -1)
    {
        Texture2D? texture = GD.Load<Texture2D>(
            $"res://images/atlases/ui_atlas.sprites/{sliceName}");
        if (texture is null)
            return null;

        // 取默认 margin；不在字典中的切片用 (0,0,0,0)
        var (defL, defT, defR, defB) = OfficialMargins.TryGetValue(sliceName, out var m)
            ? m : (0, 0, 0, 0);

        int L = marginX >= 0 ? marginX : defL;
        int R = marginX >= 0 ? marginX : defR;
        int T = marginY >= 0 ? marginY : defT;
        int B = marginY >= 0 ? marginY : defB;

        return new StyleBoxTexture
        {
            Texture = texture,
            TextureMarginLeft   = L,
            TextureMarginTop    = T,
            TextureMarginRight  = R,
            TextureMarginBottom = B,
            ModulateColor = modulate ?? Colors.White,
            AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Stretch,
            AxisStretchVertical   = StyleBoxTexture.AxisStretchMode.Stretch
        };
    }

    /// <summary>取官方切片；拿不到就回落到原来的纯色样式。</summary>
    internal static StyleBox OfficialSliceOr(
        string sliceName,
        Color background,
        Color border,
        Color? modulate = null,
        int marginX = -1,
        int marginY = -1) =>
        (StyleBox?)OfficialSliceStyle(sliceName, modulate, marginX, marginY)
        ?? PanelStyle(background, border, 1, 5);
}


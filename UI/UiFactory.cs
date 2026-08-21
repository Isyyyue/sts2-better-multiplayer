using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using BetterMultiplayer.Diagnostics;
using BetterMultiplayer.Localization;

namespace BetterMultiplayer.UI;

internal static class UiFactory
{
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
        safeArea.AddThemeConstantOverride("margin_left", 56);
        safeArea.AddThemeConstantOverride("margin_right", 56);
        safeArea.AddThemeConstantOverride("margin_top", 34);
        safeArea.AddThemeConstantOverride("margin_bottom", 34);
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
        contentMargin.AddThemeConstantOverride("margin_left", 104);
        contentMargin.AddThemeConstantOverride("margin_right", 104);
        contentMargin.AddThemeConstantOverride("margin_top", 68);
        contentMargin.AddThemeConstantOverride("margin_bottom", 58);
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
        Label label = new()
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center
        };
        label.AddThemeFontSizeOverride("font_size", size);
        if (color.HasValue)
            label.AddThemeColorOverride("font_color", color.Value);
        return label;
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
    }

    private sealed class NativeInputBinding
    {
        private enum VisualState
        {
            Normal,
            Hover,
            Pressed
        }

        private readonly Button _button;
        private readonly string? _diagnosticId;
        private readonly StyleBox? _normalStyle;
        private readonly StyleBox? _hoverStyle;
        private readonly StyleBox? _pressedStyle;

        internal NButton Input { get; }

        internal NativeInputBinding(Button button, Action onReleased, string? diagnosticId)
        {
            _button = button;
            _diagnosticId = diagnosticId;
            _normalStyle = button.GetThemeStylebox("normal");
            _hoverStyle = button.GetThemeStylebox("hover");
            _pressedStyle = button.GetThemeStylebox("pressed");

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
            SetVisualState(_button.Disabled ? VisualState.Normal : VisualState.Hover);
        }

        private void OnUnfocused(NClickableControl _)
        {
            RecordInput("unfocused");
            SetVisualState(VisualState.Normal);
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
                _ => _normalStyle
            };
            if (style is not null)
                _button.AddThemeStyleboxOverride("normal", style);
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
}

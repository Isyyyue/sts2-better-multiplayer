using System.Reflection;
using System.Reflection.Emit;
using BetterMultiplayer.UI;
using MegaCrit.Sts2.addons.mega_text;

namespace BetterMultiplayer.Tests;

public sealed class UiRegressionTests
{
    private static readonly BindingFlags StaticNonPublic =
        BindingFlags.Static | BindingFlags.NonPublic;

    private static readonly BindingFlags InstanceNonPublic =
        BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void SelectionRenderDoesNotPrependTheFullOfferBoard()
    {
        MethodInfo render = Required(
            typeof(TradeOverlay).GetMethod("Render", InstanceNonPublic));
        MethodInfo renderActive = Required(
            typeof(TradeOverlay).GetMethod("RenderActive", InstanceNonPublic));
        MethodInfo renderSelection = Required(
            typeof(TradeOverlay).GetMethod("RenderSelection", InstanceNonPublic));

        IReadOnlyList<IlInstruction> instructions = ReadInstructions(render);
        int selectionCall = IndexOfCall(render, instructions, renderSelection);
        Assert.True(selectionCall >= 0, "Render must call RenderSelection.");

        Assert.DoesNotContain(
            instructions.Take(selectionCall),
            instruction => instruction.MetadataToken == renderActive.MetadataToken);
    }

    [Fact]
    public void FactoryDisablesMegaLabelAutosizeBeforeSettingText()
    {
        MethodInfo labelFactory = Required(typeof(UiFactory).GetMethod("Label", StaticNonPublic));
        MethodInfo autoSizeSetter = Required(
            typeof(MegaLabel).GetProperty(nameof(MegaLabel.AutoSizeEnabled))?.SetMethod);
        MethodInfo setText = Required(typeof(MegaLabel).GetMethod(nameof(MegaLabel.SetTextAutoSize)));

        IReadOnlyList<IlInstruction> instructions = ReadInstructions(labelFactory);
        int autoSizeCall = IndexOfCall(labelFactory, instructions, autoSizeSetter);
        int textCall = IndexOfCall(labelFactory, instructions, setText);

        Assert.True(autoSizeCall >= 0, "UiFactory.Label must explicitly disable MegaLabel autosizing.");
        Assert.True(textCall > autoSizeCall, "Autosizing must be disabled before setting the label text.");
        Assert.True(autoSizeCall > 0);
        Assert.Equal(OpCodes.Ldc_I4_0, instructions[autoSizeCall - 1].OpCode);
    }

    private static int IndexOfCall(
        MethodInfo owner,
        IReadOnlyList<IlInstruction> instructions,
        MethodInfo target)
    {
        for (int index = 0; index < instructions.Count; index++)
        {
            IlInstruction instruction = instructions[index];
            if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
                continue;

            if (instruction.MetadataToken == target.MetadataToken && owner.Module == target.Module)
                return index;

            if (instruction.MetadataToken is int token &&
                TryResolveMethod(owner.Module, token) is MethodBase resolved &&
                resolved.Name == target.Name &&
                resolved.DeclaringType?.FullName == target.DeclaringType?.FullName)
                return index;
        }

        return -1;
    }

    private static MethodBase? TryResolveMethod(Module module, int token)
    {
        try
        {
            return module.ResolveMethod(token);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static MethodInfo Required(MethodInfo? method) =>
        method ?? throw new InvalidOperationException("Expected production method was not found.");

    private static IReadOnlyList<IlInstruction> ReadInstructions(MethodInfo method)
    {
        byte[] il = method.GetMethodBody()?.GetILAsByteArray() ?? [];
        Dictionary<short, OpCode> opCodes = typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(opCode => opCode.Value);
        List<IlInstruction> instructions = [];
        int offset = 0;

        while (offset < il.Length)
        {
            short value = il[offset++] == 0xfe
                ? unchecked((short)(0xfe00 | il[offset++]))
                : il[offset - 1];
            OpCode opCode = opCodes[value];
            int operandOffset = offset;
            int operandSize = OperandSize(opCode.OperandType, il, operandOffset);
            int? metadataToken = opCode.OperandType == OperandType.InlineMethod
                ? BitConverter.ToInt32(il, operandOffset)
                : null;
            instructions.Add(new IlInstruction(opCode, metadataToken));
            offset += operandSize;
        }

        return instructions;
    }

    private static int OperandSize(OperandType operandType, byte[] il, int offset) =>
        operandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or
                OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineI or
                OperandType.InlineMethod or OperandType.InlineType or OperandType.InlineSig or OperandType.InlineString or
                OperandType.InlineTok or OperandType.ShortInlineR => 4,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch => 4 + BitConverter.ToInt32(il, offset) * 4,
            _ => throw new InvalidOperationException($"Unsupported IL operand type: {operandType}")
        };

    private readonly record struct IlInstruction(OpCode OpCode, int? MetadataToken);

    [Fact]
    public void RestOfferIsBoundToDedicatedCardRelicAndPotionRows()
    {
        MethodInfo restOffer = Required(
            typeof(TradeOverlay).GetMethod("CreateRestOffer", InstanceNonPublic));
        IReadOnlyList<IlInstruction> instructions = ReadInstructions(restOffer);

        Assert.Equal(2, CountCalls(restOffer, instructions, "CreateSingleItemRow"));
        Assert.Equal(1, CountCalls(restOffer, instructions, "CreateOfferRow"));
    }

    [Fact]
    public void OfferPanelUsesPlayerHeaderAndSeparateContentBuilder()
    {
        MethodInfo panel = Required(
            typeof(TradeOverlay).GetMethod("CreateOfferPanel", InstanceNonPublic));
        IReadOnlyList<IlInstruction> instructions = ReadInstructions(panel);

        Assert.True(CountCalls(panel, instructions, "CreatePlayerHeader") > 0);
        Assert.True(CountCalls(panel, instructions, "CreateRestOffer") > 0);
        Assert.True(CountCalls(panel, instructions, "CreateGoldOffer") > 0);
    }

    private static int CountCalls(
        MethodInfo owner,
        IReadOnlyList<IlInstruction> instructions,
        string methodName)
    {
        return instructions.Count(instruction =>
            (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) &&
            instruction.MetadataToken is int token &&
            TryResolveMethod(owner.Module, token)?.Name == methodName);
    }
}

using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Players;

namespace BetterMultiplayer.Trading;

internal static class GameApiCompatibility
{
    internal static bool CanRemovePotions(Player player) =>
        TryReadBooleanProperty(player, "CanRemovePotions") ??
        TryReadBooleanProperty(player, "CanUseOrRemovePotions") ??
        false;

    internal static bool IsUsingController(object? manager)
    {
        if (manager is null)
            return false;

        bool? legacyValue = TryReadBooleanProperty(manager, "IsUsingController");
        if (legacyValue.HasValue)
            return legacyValue.Value;

        object? inputType = TryReadProperty(manager, "InputType");
        return string.Equals(inputType?.ToString(), "Controller", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool? TryReadBooleanProperty(object target, string propertyName)
    {
        object? value = TryReadProperty(target, propertyName);
        return value is bool boolean ? boolean : null;
    }

    private static object? TryReadProperty(object target, string propertyName)
    {
        try
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            PropertyInfo? property = target.GetType().GetProperty(propertyName, flags);
            if (property is not null)
                return property.GetValue(target);

            return target.GetType().GetField(propertyName, flags)?.GetValue(target);
        }
        catch (TargetInvocationException)
        {
            return null;
        }
    }
}

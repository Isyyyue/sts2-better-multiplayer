using System.Globalization;
using System.Text;

namespace BetterMultiplayer.Lobby;

public static class RoomText
{
    public const int MaxRoomNameLength = 32;
    public const int MaxPasswordLength = 64;

    public static string NormalizeRoomName(string input)
    {
        string normalized = (input ?? string.Empty).Normalize(NormalizationForm.FormKC).Trim();
        StringBuilder result = new(MaxRoomNameLength);

        foreach (char value in normalized)
        {
            UnicodeCategory category = char.GetUnicodeCategory(value);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate)
                continue;
            if (result.Length >= MaxRoomNameLength)
                break;
            result.Append(value);
        }

        return result.ToString();
    }

    public static string NormalizeSearch(string input) => NormalizeRoomName(input).ToUpperInvariant();

    public static bool IsValidPassword(string password) =>
        password.Length <= MaxPasswordLength && password.All(c => !char.IsControl(c));
}

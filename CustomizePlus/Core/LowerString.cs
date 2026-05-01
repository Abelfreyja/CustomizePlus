using Dalamud.Bindings.ImGui;
using Newtonsoft.Json;

namespace CustomizePlus;

[JsonConverter(typeof(Converter))]
public readonly struct LowerString : IEquatable<LowerString>, IComparable<LowerString>, IEquatable<string>, IComparable<string>
{
    public static readonly LowerString Empty = new(string.Empty);

    public readonly string Text;
    public readonly string Lower;

    public LowerString(string text)
    {
        Text = string.Intern(text);
        Lower = string.Intern(text.ToLowerInvariant());
    }

    public int Length
        => Text.Length;

    public bool IsEmpty
        => Length == 0;

    public bool Equals(LowerString other)
        => string.Equals(Lower, other.Lower, StringComparison.Ordinal);

    public bool Equals(string? other)
        => string.Equals(Lower, other, StringComparison.OrdinalIgnoreCase);

    public int CompareTo(LowerString other)
        => string.Compare(Lower, other.Lower, StringComparison.Ordinal);

    public int CompareTo(string? other)
        => string.Compare(Lower, other, StringComparison.OrdinalIgnoreCase);

    public bool Contains(LowerString other)
        => Lower.Contains(other.Lower, StringComparison.Ordinal);

    public bool Contains(string other)
        => Lower.Contains(other, StringComparison.OrdinalIgnoreCase);

    public bool IsContained(string other)
        => IsEmpty || other.Contains(Lower, StringComparison.OrdinalIgnoreCase);

    public override string ToString()
        => Text;

    public static implicit operator string(LowerString value)
        => value.Text;

    public static implicit operator LowerString(string value)
        => new(value);

    public static bool InputWithHint(string label, string hint, ref LowerString value, int maxLength = 128,
        ImGuiInputTextFlags flags = ImGuiInputTextFlags.None)
    {
        var text = value.Text;
        if (!ImGui.InputTextWithHint(label, hint, ref text, maxLength, flags) || text == value.Text)
            return false;

        value = new LowerString(text);
        return true;
    }

    public override bool Equals(object? obj)
        => obj is LowerString lowerString && Equals(lowerString);

    public override int GetHashCode()
        => Text.GetHashCode();

    private sealed class Converter : JsonConverter<LowerString>
    {
        public override void WriteJson(JsonWriter writer, LowerString value, JsonSerializer serializer)
            => writer.WriteValue(value.Text);

        public override LowerString ReadJson(JsonReader reader, Type objectType, LowerString existingValue, bool hasExistingValue,
            JsonSerializer serializer)
            => reader.Value is string text ? new LowerString(text) : existingValue;
    }
}





using Dalamud.Game.ClientState.Keys;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CustomizePlus.Core.Helpers;

internal sealed class DoubleModifierJsonConverter : JsonConverter<DoubleModifier>
{
    public override void WriteJson(JsonWriter writer, DoubleModifier value, JsonSerializer serializer)
    {
        writer.WriteStartObject();
        writer.WritePropertyName(nameof(DoubleModifier.Modifier1));
        writer.WriteValue((ushort)value.Modifier1.Modifier);

        if (value.Modifier2.Modifier != ModifierHotkey.NoKey)
        {
            writer.WritePropertyName(nameof(DoubleModifier.Modifier2));
            writer.WriteValue((ushort)value.Modifier2.Modifier);
        }

        writer.WriteEndObject();
    }

    public override DoubleModifier ReadJson(
        JsonReader reader,
        Type objectType,
        DoubleModifier existingValue,
        bool hasExistingValue,
        JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return existingValue;

        var token = JToken.Load(reader);
        if (token.Type is not JTokenType.Object)
            return existingValue;

        var modifier1 = ReadModifier(token[nameof(DoubleModifier.Modifier1)]);
        var modifier2 = ReadModifier(token[nameof(DoubleModifier.Modifier2)]);

        return new DoubleModifier(modifier1, modifier2);
    }

    private static ModifierHotkey ReadModifier(JToken? token)
    {
        if (token is null || token.Type == JTokenType.Null)
            return ModifierHotkey.NoKey;

        if (token.Type is JTokenType.Integer)
            return new ModifierHotkey((VirtualKey)token.Value<ushort>());

        if (token.Type is JTokenType.String && ushort.TryParse(token.Value<string>(), out var value))
            return new ModifierHotkey((VirtualKey)value);

        if (token is JObject obj && obj.TryGetValue(nameof(ModifierHotkey.Modifier), out var modifier))
            return ReadModifier(modifier);

        return ModifierHotkey.NoKey;
    }
}

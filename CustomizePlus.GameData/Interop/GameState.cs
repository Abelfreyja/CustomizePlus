namespace CustomizePlus.GameData.Interop;

public class GameState : Luna.IService
{
    public readonly ThreadLocal<bool> CharacterAssociated = new(() => false);
}

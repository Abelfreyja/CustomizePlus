using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Luna;
using Penumbra.GameData;

namespace CustomizePlus.GameData.Hooks.Objects;
public sealed unsafe class CharacterDestructor : EventBase<CharacterDestructor.Arguments, CharacterDestructor.Priority>, IHookService
{
    public readonly struct Arguments(Character* character)
    {
        public readonly Character* Character = character;
    }

    public enum Priority
    {
        /// <seealso cref="PathResolving.CutsceneService"/>
        CutsceneService = 0,

        /// <seealso cref="PathResolving.IdentifiedCollectionCache"/>
        IdentifiedCollectionCache = 0,
    }

    public CharacterDestructor(HookManager hooks, LunaLogger log)
        : base("Character Destructor", log)
        => _task = hooks.CreateHook<Delegate>(Name, Sigs.CharacterDestructor, Detour, true);

    private readonly Task<Hook<Delegate>> _task;

    public nint Address
        => _task.Result.Address;

    public void Enable()
        => _task.Result.Enable();

    public void Disable()
        => _task.Result.Disable();

    public Task Awaiter
        => _task;

    public bool Finished
        => _task.IsCompletedSuccessfully;

    private delegate void Delegate(Character* character);

    private void Detour(Character* character)
    {
        //Penumbra.Log.Verbose($"[{Name}] Triggered with 0x{(nint)character:X}.");
        Invoke(new Arguments(character));
        _task.Result.Original(character);
    }
}
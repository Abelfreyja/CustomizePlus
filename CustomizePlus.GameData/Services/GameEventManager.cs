using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Penumbra.GameData;

namespace CustomizePlus.GameData.Services;

public unsafe class GameEventManager : IDisposable
{
    private const string Prefix = $"[{nameof(GameEventManager)}]";

    public event CharacterDestructorEvent? CharacterDestructor;
    public event CopyCharacterEvent? CopyCharacter;
    public event CreatingCharacterBaseEvent? CreatingCharacterBase;
    public event CharacterBaseCreatedEvent? CharacterBaseCreated;
    public event CharacterBaseDestructorEvent? CharacterBaseDestructor;

    public GameEventManager(IGameInteropProvider interop)
    {
        interop.InitializeFromAttributes(this);

        _copyCharacterHook =
            interop.HookFromAddress<CopyCharacterDelegate>((nint)CharacterSetupContainer.MemberFunctionPointers.CopyFromCharacter, CopyCharacterDetour);
        _characterBaseCreateHook =
            interop.HookFromAddress<CharacterBaseCreateDelegate>((nint)CharacterBase.MemberFunctionPointers.Create, CharacterBaseCreateDetour);
        _characterBaseDestructorHook =
            interop.HookFromAddress<CharacterBaseDestructorEvent>((nint)CharacterBase.MemberFunctionPointers.Destroy,
                CharacterBaseDestructorDetour);
        _characterDtorHook.Enable();
        _copyCharacterHook.Enable();
        _characterBaseCreateHook.Enable();
        _characterBaseDestructorHook.Enable();
    }

    public void Dispose()
    {
        _characterDtorHook.Dispose();
        _copyCharacterHook.Dispose();
        _characterBaseCreateHook.Dispose();
        _characterBaseDestructorHook.Dispose();
    }

    #region Character Destructor

    private delegate void CharacterDestructorDelegate(Character* character);

    [Signature(Sigs.CharacterDestructor, DetourName = nameof(CharacterDestructorDetour))]
    private readonly Hook<CharacterDestructorDelegate> _characterDtorHook = null!;

    private void CharacterDestructorDetour(Character* character)
    {
        if (CharacterDestructor != null)
            foreach (var subscriber in CharacterDestructor.GetInvocationList())
            {
                try
                {
                    ((CharacterDestructorEvent)subscriber).Invoke(character);
                }
                catch (Exception ex)
                {
                    //Penumbra.Log.Error($"{Prefix} Error in {nameof(CharacterDestructor)} event when executing {subscriber.Method.Name}:\n{ex}");
                    //todo: log
                }
            }

        //Penumbra.Log.Verbose($"{Prefix} {nameof(CharacterDestructor)} triggered with 0x{(nint)character:X}.");
        //todo: log
        _characterDtorHook.Original(character);
    }

    public delegate void CharacterDestructorEvent(Character* character);

    #endregion

    #region Copy Character

    private delegate ulong CopyCharacterDelegate(CharacterSetupContainer* target, Character* source, CharacterSetupContainer.CopyFlags flags);

    private readonly Hook<CopyCharacterDelegate> _copyCharacterHook;

    private ulong CopyCharacterDetour(CharacterSetupContainer* target, Character* source, CharacterSetupContainer.CopyFlags flags)
    {
        var character = target->OwnerObject;
        if (CopyCharacter != null)
            foreach (var subscriber in CopyCharacter.GetInvocationList())
            {
                try
                {
                    ((CopyCharacterEvent)subscriber).Invoke(character, source);
                }
                catch (Exception ex)
                {
                    /*Penumbra.Log.Error(
                        $"{Prefix} Error in {nameof(CopyCharacter)} event when executing {subscriber.Method.Name}:\n{ex}");*/
                    //todo: log
                }
            }

        /*Penumbra.Log.Verbose(
            $"{Prefix} {nameof(CopyCharacter)} triggered with target 0x{(nint)target:X}, source 0x{(nint)source:X} and flags {flags}.");*/
        //todo: log
        return _copyCharacterHook.Original(target, source, flags);
    }

    public delegate void CopyCharacterEvent(Character* target, Character* source);

    #endregion

    #region CharacterBaseCreate

    private delegate CharacterBase* CharacterBaseCreateDelegate(uint modelId, CustomizeData* customize, EquipmentModelId* equipment, byte unk);

    private readonly Hook<CharacterBaseCreateDelegate> _characterBaseCreateHook;

    private CharacterBase* CharacterBaseCreateDetour(uint modelId, CustomizeData* customize, EquipmentModelId* equipment, byte unk)
    {
        if (CreatingCharacterBase != null)
            foreach (var subscriber in CreatingCharacterBase.GetInvocationList())
            {
                try
                {
                    ((CreatingCharacterBaseEvent)subscriber).Invoke(&modelId, customize, equipment);
                }
                catch (Exception ex)
                {
                    /*Penumbra.Log.Error(
                        $"{Prefix} Error in {nameof(CharacterBaseCreateDetour)} event when executing {subscriber.Method.Name}:\n{ex}");*/
                    //todo: log
                }
            }

        var ret = _characterBaseCreateHook.Original(modelId, customize, equipment, unk);
        if (CharacterBaseCreated != null)
            foreach (var subscriber in CharacterBaseCreated.GetInvocationList())
            {
                try
                {
                    ((CharacterBaseCreatedEvent)subscriber).Invoke(modelId, customize, equipment, ret);
                }
                catch (Exception ex)
                {
                    /*Penumbra.Log.Error(
                        $"{Prefix} Error in {nameof(CharacterBaseCreateDetour)} event when executing {subscriber.Method.Name}:\n{ex}");*/
                    //todo: log
                }
            }

        return ret;
    }

    public delegate void CreatingCharacterBaseEvent(uint* modelCharaId, CustomizeData* customize, EquipmentModelId* equipment);
    public delegate void CharacterBaseCreatedEvent(uint modelCharaId, CustomizeData* customize, EquipmentModelId* equipment, CharacterBase* drawObject);

    #endregion

    #region CharacterBase Destructor

    public delegate void CharacterBaseDestructorEvent(CharacterBase* drawBase);

    private readonly Hook<CharacterBaseDestructorEvent> _characterBaseDestructorHook;

    private void CharacterBaseDestructorDetour(CharacterBase* drawBase)
    {
        if (CharacterBaseDestructor != null)
            foreach (var subscriber in CharacterBaseDestructor.GetInvocationList())
            {
                try
                {
                    ((CharacterBaseDestructorEvent)subscriber).Invoke(drawBase);
                }
                catch (Exception ex)
                {
                    /*Penumbra.Log.Error(
                        $"{Prefix} Error in {nameof(CharacterBaseDestructorDetour)} event when executing {subscriber.Method.Name}:\n{ex}");*/
                    //todo: log
                }
            }

        _characterBaseDestructorHook.Original.Invoke(drawBase);
    }

    #endregion
}

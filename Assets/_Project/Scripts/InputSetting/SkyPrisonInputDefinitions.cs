using System;
using UnityEngine;

public enum SkyPrisonInputAction
{
    MoveUp = 0,
    MoveDown = 1,
    MoveLeft = 2,
    MoveRight = 3,

    Sprint = 10,
    Sneak = 11,
    Dodge = 12,
    Interact = 13,
    Jump = 14,

    LightAttack = 20,
    HeavyAttack = 21,
    Skill1 = 22,
    Skill2 = 23,
    Skill3 = 24,
    Reload = 25,    // 换弹（默认 R）

    Inventory = 30,
    Map = 31,
    Menu = 32,
    CharacterPanel = 33,

    CyclePickup = 50,   // 拾取目标切换（默认 R）

    QuickItem1 = 40,
    QuickItem2 = 41,
    QuickItem3 = 42,
    QuickItem4 = 43,
}

public enum SkyPrisonInputTriggerMode
{
    Hold = 0,
    Press = 1,
    Release = 2,
}

[Serializable]
public class SkyPrisonInputBinding
{
    public SkyPrisonInputAction action;
    public string displayName;
    public SkyPrisonInputTriggerMode triggerMode = SkyPrisonInputTriggerMode.Hold;

    [Header("Keyboard / Mouse")]
    public KeyCode primaryKey = KeyCode.None;
    public KeyCode secondaryKey = KeyCode.None;

    [Header("Gamepad")]
    public KeyCode gamepadKey = KeyCode.None;

    public SkyPrisonInputBinding() { }

    public SkyPrisonInputBinding(
        SkyPrisonInputAction action,
        string displayName,
        KeyCode primary,
        KeyCode secondary = KeyCode.None,
        KeyCode gamepad = KeyCode.None,
        SkyPrisonInputTriggerMode trigger = SkyPrisonInputTriggerMode.Hold)
    {
        this.action = action;
        this.displayName = displayName;
        this.primaryKey = primary;
        this.secondaryKey = secondary;
        this.gamepadKey = gamepad;
        this.triggerMode = trigger;
    }
}

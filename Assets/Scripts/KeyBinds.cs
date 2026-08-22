using System;
using UnityEngine;

/// <summary>
/// Every key the game reads, in one place, rebindable and saved.
///
/// Before this the keys were scattered as literals across four scripts, and movement came from
/// Unity's Input Manager axes - which can only be changed in a project settings window, not by
/// the person playing. WASD is not a universal preference and neither is shift-to-walk.
///
/// Everything is expressed as a KeyCode, including the mouse buttons, because KeyCode.Mouse0
/// and friends work through the same Input.GetKey calls. That means the whole scheme is one
/// uniform table rather than a special case per input device, and rebinding fire to a keyboard
/// key or jump to a mouse button both fall out for free.
///
/// The cost, stated plainly: reading movement from four discrete keys instead of Horizontal and
/// Vertical gives up analog sticks. This is a mouse and keyboard game played by five people, and
/// being able to rebind is worth more than gamepad support nobody asked for.
/// </summary>
public static class KeyBinds
{
    public enum Action
    {
        Forward,
        Back,
        Left,
        Right,
        Jump,
        /// Slide when you are moving, crouch when you are not. Called Walk in the enum for one
        /// release's worth of saved bindings; the label is what people read.
        Walk,
        Fire,
        Aim,
        Reload,
        NextWeapon,
        PreviousWeapon,
        Scoreboard,
        Menu,
        /// Fire and hold to latch onto a vantage point or an enemy; release to let go early.
        /// Appended at the end rather than sorted in with the rest, so every existing binding
        /// keeps the same enum value.
        Grapple,
        /// <summary>
        /// The ground slam, on its own key as of 2026-08-22. It used to share Walk with slide and
        /// crouch, told apart by vertical velocity - falling meant slam, rising meant air brake -
        /// which sounded clean in the abstract and was wrong in practice: landing is *always*
        /// falling, so trying to press Walk to buffer a slide for the landing kept firing a slam
        /// instead, on every single jump. Same "appended at the end" reasoning as Grapple above.
        /// </summary>
        GroundPound,
    }

    /// Raised when a binding changes, so anything showing one can redraw.
    public static event Action<Action> Rebound;

    static readonly KeyCode[] Defaults =
    {
        KeyCode.W,          // Forward
        KeyCode.S,          // Back
        KeyCode.A,          // Left
        KeyCode.D,          // Right
        KeyCode.Space,      // Jump
        KeyCode.LeftShift,  // Slide at speed, crouch standing still
        KeyCode.Mouse0,     // Fire
        KeyCode.Mouse1,     // Aim
        KeyCode.R,          // Reload
        KeyCode.E,          // NextWeapon
        KeyCode.Q,          // PreviousWeapon
        KeyCode.Tab,        // Scoreboard
        KeyCode.Escape,     // Menu
        KeyCode.G,          // Grapple
        KeyCode.LeftControl, // GroundPound
    };

    static readonly KeyCode[] bound = (KeyCode[])Defaults.Clone();

    const string Prefix = "gw_bind_";

    /// <summary>
    /// Which actions refuse to be unbound.
    ///
    /// Losing the menu key means losing the only way back to the settings screen that could fix
    /// it, and losing fire means the game is over. Everything else can be bound to anything,
    /// including nothing.
    /// </summary>
    public static bool IsLocked(Action action) => action == Action.Menu;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Load()
    {
        for (int i = 0; i < bound.Length; i++)
        {
            string saved = PlayerPrefs.GetString(Prefix + (Action)i, string.Empty);

            // Enum.TryParse rather than a cast: KeyCode values are not contiguous, and a saved
            // string from an older build naming a key that no longer exists should fall back
            // rather than resolve to whatever integer happens to sit there.
            bound[i] = !string.IsNullOrEmpty(saved) && Enum.TryParse(saved, out KeyCode key)
                ? key
                : Defaults[i];
        }
    }

    public static KeyCode Get(Action action) => bound[(int)action];

    public static KeyCode Default(Action action) => Defaults[(int)action];

    public static void Set(Action action, KeyCode key)
    {
        if (IsLocked(action))
            return;

        // Two actions on one key is a real conflict and the loser has to give it up, or you get
        // a binding that fires two things and no way to tell which one you meant.
        for (int i = 0; i < bound.Length; i++)
        {
            if (i == (int)action || bound[i] != key || IsLocked((Action)i))
                continue;

            bound[i] = KeyCode.None;
            PlayerPrefs.SetString(Prefix + (Action)i, KeyCode.None.ToString());
            Rebound?.Invoke((Action)i);
        }

        bound[(int)action] = key;
        PlayerPrefs.SetString(Prefix + action, key.ToString());
        PlayerPrefs.Save();

        Rebound?.Invoke(action);
    }

    public static void ResetAll()
    {
        for (int i = 0; i < bound.Length; i++)
        {
            bound[i] = Defaults[i];
            PlayerPrefs.DeleteKey(Prefix + (Action)i);
            Rebound?.Invoke((Action)i);
        }

        PlayerPrefs.Save();
    }

    // ---------------------------------------------------------------- reading

    // KeyCode.None is a legitimate binding - it means the action is switched off - and asking
    // Input about it every frame is a waste, so all three short circuit.

    public static bool Held(Action action)
    {
        KeyCode key = bound[(int)action];
        return key != KeyCode.None && Input.GetKey(key);
    }

    public static bool Pressed(Action action)
    {
        KeyCode key = bound[(int)action];
        return key != KeyCode.None && Input.GetKeyDown(key);
    }

    public static bool Released(Action action)
    {
        KeyCode key = bound[(int)action];
        return key != KeyCode.None && Input.GetKeyUp(key);
    }

    /// Movement as a direction, built from the four bound keys rather than from the Input
    /// Manager's axes - which is the whole reason movement can be rebound at all.
    public static Vector2 MoveAxis()
    {
        float x = (Held(Action.Right) ? 1f : 0f) - (Held(Action.Left) ? 1f : 0f);
        float y = (Held(Action.Forward) ? 1f : 0f) - (Held(Action.Back) ? 1f : 0f);

        return new Vector2(x, y);
    }

    /// <summary>
    /// A readable name for a binding, for the settings screen.
    ///
    /// KeyCode's own names are close enough for letters and useless for everything else -
    /// "Mouse0" is not what anyone calls left click, and "Alpha1" is not a key on any keyboard.
    /// </summary>
    public static string Label(KeyCode key)
    {
        switch (key)
        {
            case KeyCode.None: return "unbound";
            case KeyCode.Mouse0: return "LEFT CLICK";
            case KeyCode.Mouse1: return "RIGHT CLICK";
            case KeyCode.Mouse2: return "MIDDLE CLICK";
            case KeyCode.Mouse3: return "MOUSE 4";
            case KeyCode.Mouse4: return "MOUSE 5";
            case KeyCode.LeftShift: return "L SHIFT";
            case KeyCode.RightShift: return "R SHIFT";
            case KeyCode.LeftControl: return "L CTRL";
            case KeyCode.RightControl: return "R CTRL";
            case KeyCode.LeftAlt: return "L ALT";
            case KeyCode.RightAlt: return "R ALT";
            case KeyCode.Space: return "SPACE";
            case KeyCode.Escape: return "ESC";
            case KeyCode.Return: return "ENTER";
        }

        string name = key.ToString();

        // Alpha1 -> 1, Keypad1 -> NUM 1.
        if (name.StartsWith("Alpha"))
            return name.Substring(5);

        if (name.StartsWith("Keypad"))
            return "NUM " + name.Substring(6);

        return name.ToUpper();
    }

    public static string Label(Action action) => Label(Get(action));

    /// <summary>
    /// A human name for the action itself.
    ///
    /// Spelled out rather than derived from the enum, because "PreviousWeapon" is not a phrase
    /// and this is read by a person deciding what to press.
    /// </summary>
    public static string Describe(Action action)
    {
        switch (action)
        {
            case Action.Forward: return "move forward";
            case Action.Back: return "move back";
            case Action.Left: return "strafe left";
            case Action.Right: return "strafe right";
            case Action.Jump: return "jump";
            case Action.Walk: return "slide / crouch (hold)";
            case Action.Fire: return "fire";
            case Action.Aim: return "aim";
            case Action.Reload: return "reload";
            case Action.NextWeapon: return "next weapon";
            case Action.PreviousWeapon: return "previous weapon";
            case Action.Scoreboard: return "scoreboard (hold)";
            case Action.Menu: return "menu";
            case Action.Grapple: return "grapple (hold)";
            case Action.GroundPound: return "ground pound";
            default: return action.ToString();
        }
    }
}

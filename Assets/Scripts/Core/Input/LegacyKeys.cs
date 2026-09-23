using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace RythmRPG.Core
{
    /// <summary>
    /// Reads <see cref="KeyCode"/> values through the new Input System, for fixed keys that stay configured as
    /// KeyCode fields in the inspector (debug shortcuts, result-screen keys). Rebindable game controls live in
    /// <see cref="GameInput"/> instead.
    /// </summary>
    public static class LegacyKeys
    {
        private static readonly Dictionary<KeyCode, Key> cache = new();

        public static bool IsHeld(KeyCode keyCode)
        {
            ButtonControl control = Control(keyCode);
            return control != null && control.isPressed;
        }

        public static bool WasPressed(KeyCode keyCode)
        {
            ButtonControl control = Control(keyCode);
            return control != null && control.wasPressedThisFrame;
        }

        /// <summary>Any key, mouse button or gamepad button went down this frame (like <c>Input.anyKeyDown</c>).</summary>
        public static bool AnyKeyDown()
        {
            if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) return true;
            Mouse mouse = Mouse.current;
            if (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame
                || mouse.middleButton.wasPressedThisFrame)) return true;
            Gamepad pad = Gamepad.current;
            if (pad == null) return false;
            return pad.buttonSouth.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame
                || pad.buttonWest.wasPressedThisFrame || pad.buttonNorth.wasPressedThisFrame
                || pad.startButton.wasPressedThisFrame || pad.selectButton.wasPressedThisFrame
                || pad.leftShoulder.wasPressedThisFrame || pad.rightShoulder.wasPressedThisFrame;
        }

        public static ButtonControl Control(KeyCode keyCode)
        {
            switch (keyCode)
            {
                case KeyCode.None: return null;
                case KeyCode.Mouse0: return Mouse.current?.leftButton;
                case KeyCode.Mouse1: return Mouse.current?.rightButton;
                case KeyCode.Mouse2: return Mouse.current?.middleButton;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return null;
            Key key = ToKey(keyCode);
            return key == Key.None ? null : keyboard[key];
        }

        public static Key ToKey(KeyCode keyCode)
        {
            if (cache.TryGetValue(keyCode, out Key cached)) return cached;
            Key key = Translate(keyCode);
            cache[keyCode] = key;
            return key;
        }

        private static Key Translate(KeyCode keyCode)
        {
            if (keyCode >= KeyCode.Alpha0 && keyCode <= KeyCode.Alpha9)
                return Parse("Digit" + (keyCode - KeyCode.Alpha0));
            if (keyCode >= KeyCode.Keypad0 && keyCode <= KeyCode.Keypad9)
                return Parse("Numpad" + (keyCode - KeyCode.Keypad0));

            switch (keyCode)
            {
                case KeyCode.Return: return Key.Enter;
                case KeyCode.KeypadEnter: return Key.NumpadEnter;
                case KeyCode.KeypadPlus: return Key.NumpadPlus;
                case KeyCode.KeypadMinus: return Key.NumpadMinus;
                case KeyCode.KeypadMultiply: return Key.NumpadMultiply;
                case KeyCode.KeypadDivide: return Key.NumpadDivide;
                case KeyCode.KeypadPeriod: return Key.NumpadPeriod;
                case KeyCode.KeypadEquals: return Key.NumpadEquals;
                case KeyCode.LeftControl: return Key.LeftCtrl;
                case KeyCode.RightControl: return Key.RightCtrl;
                case KeyCode.LeftCommand: return Key.LeftMeta;
                case KeyCode.RightCommand: return Key.RightMeta;
                case KeyCode.BackQuote: return Key.Backquote;
                case KeyCode.Menu: return Key.ContextMenu;
                case KeyCode.Print: return Key.PrintScreen;
                case KeyCode.Numlock: return Key.NumLock;
                case KeyCode.CapsLock: return Key.CapsLock;
                case KeyCode.ScrollLock: return Key.ScrollLock;
            }

            return Parse(keyCode.ToString());
        }

        private static Key Parse(string name) => Enum.TryParse(name, true, out Key key) ? key : Key.None;
    }
}

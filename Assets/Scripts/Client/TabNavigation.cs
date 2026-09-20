using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CubeArena.Client
{
    // Tab / Shift+Tab cycles focus between a fixed set of input fields. The new Input
    // System's InputSystemUIInputModule only wires arrow-key/gamepad navigation by
    // default, not Tab, so this fills that gap for keyboard-only users. Only runs while
    // its panel is active (Update doesn't fire on an inactive GameObject), so it's safe
    // to attach to every panel without the fields bleeding across screens.
    public class TabNavigation : MonoBehaviour
    {
        private InputField[] _fields;

        public void SetFields(params InputField[] fields) => _fields = fields;

        private void Update()
        {
            if (_fields == null || _fields.Length < 2 || Keyboard.current == null)
            {
                return;
            }

            if (!Keyboard.current.tabKey.wasPressedThisFrame)
            {
                return;
            }

            var current = EventSystem.current.currentSelectedGameObject;
            var currentIndex = Array.FindIndex(_fields, f => f != null && f.gameObject == current);
            var direction = Keyboard.current.shiftKey.isPressed ? -1 : 1;
            var nextIndex = currentIndex < 0 ? 0 : (currentIndex + direction + _fields.Length) % _fields.Length;

            _fields[nextIndex].Select();
            _fields[nextIndex].ActivateInputField();
        }
    }
}

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public class AimInputController : MonoBehaviour
{
    public enum PointerSource
    {
        None,
        Mouse,
        Touch
    }

    public struct PointerFrameState
    {
        public PointerSource Source;
        public Vector2 ScreenPosition;
        public bool PressedThisFrame;
        public bool ReleasedThisFrame;
    }

    public PointerSource ActivePointerSource { get; private set; }

    public void SetActivePointerSource(PointerSource source)
    {
        ActivePointerSource = source;
    }

    public void ClearActivePointerSource()
    {
        ActivePointerSource = PointerSource.None;
    }

    public bool TryGetPointerScreenPosition(out Vector2 pointerScreen)
    {
        if (TryGetPointerFrameState(out PointerFrameState pointerState))
        {
            pointerScreen = pointerState.ScreenPosition;
            return true;
        }

        pointerScreen = Vector2.zero;
        return false;
    }

    public bool TryGetPointerFrameState(out PointerFrameState pointerState)
    {
        if (ActivePointerSource == PointerSource.Touch)
        {
            return TryGetTouchPointerState(out pointerState);
        }

        if (ActivePointerSource == PointerSource.Mouse)
        {
            return TryGetMousePointerState(out pointerState);
        }

        if (TryGetTouchPointerState(out pointerState))
        {
            return true;
        }

        if (TryGetMousePointerState(out pointerState))
        {
            return true;
        }

        pointerState = default;
        return false;
    }

    private bool TryGetTouchPointerState(out PointerFrameState pointerState)
    {
        pointerState = default;

        Touchscreen touchScreen = Touchscreen.current;
        if (touchScreen == null)
        {
            return false;
        }

        TouchControl touch = touchScreen.primaryTouch;
        if (touch == null)
        {
            return false;
        }

        bool isActive = touch.press.isPressed ||
                        touch.press.wasPressedThisFrame ||
                        touch.press.wasReleasedThisFrame;
        if (!isActive)
        {
            return false;
        }

        pointerState.Source = PointerSource.Touch;
        pointerState.ScreenPosition = touch.position.ReadValue();
        pointerState.PressedThisFrame = touch.press.wasPressedThisFrame;
        pointerState.ReleasedThisFrame = touch.press.wasReleasedThisFrame;
        return true;
    }

    private bool TryGetMousePointerState(out PointerFrameState pointerState)
    {
        pointerState = default;

        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            return false;
        }

        bool isActive = mouse.leftButton.isPressed ||
                        mouse.leftButton.wasPressedThisFrame ||
                        mouse.leftButton.wasReleasedThisFrame;
        if (!isActive)
        {
            return false;
        }

        pointerState.Source = PointerSource.Mouse;
        pointerState.ScreenPosition = mouse.position.ReadValue();
        pointerState.PressedThisFrame = mouse.leftButton.wasPressedThisFrame;
        pointerState.ReleasedThisFrame = mouse.leftButton.wasReleasedThisFrame;
        return true;
    }
}

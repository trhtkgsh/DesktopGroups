using System.Runtime.InteropServices;

namespace DesktopGroups;

// Global low-level keyboard hook. Raises Pressed for Left Alt + ~ wherever the focus is, and swallows those keys.
public sealed class KeyboardHook : IDisposable
{
    private readonly NativeMethods.LowLevelKeyboardProc callback;
    private readonly IntPtr hook;
    private bool leftAltDown;
    private bool tildeDown;

    public KeyboardHook()
    {
        callback = HookCallback;
        hook = NativeMethods.SetWindowsHookEx(NativeMethods.WhKeyboardLl, callback,
            Marshal.GetHINSTANCE(typeof(KeyboardHook).Module), 0);
    }

    public event Action? Pressed;

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return NativeMethods.CallNextHookEx(hook, nCode, wParam, lParam);

        var info = Marshal.PtrToStructure<NativeMethods.KbdLlHookStruct>(lParam);
        var message = wParam.ToInt32();
        var isDown = message is NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown;
        var isUp = message is NativeMethods.WmKeyUp or NativeMethods.WmSysKeyUp;

        if (info.VkCode == NativeMethods.VkLeftMenu)
        {
            if (isDown) leftAltDown = true;
            else if (isUp) leftAltDown = false;
        }
        else if (info.VkCode == NativeMethods.VkOem3 && leftAltDown)
        {
            if (isDown)
            {
                // Ignores auto-repeat so holding the keys does not toggle repeatedly.
                if (!tildeDown)
                {
                    tildeDown = true;
                    Pressed?.Invoke();
                }
                return 1;
            }
            if (isUp)
            {
                tildeDown = false;
                return 1;
            }
        }

        return NativeMethods.CallNextHookEx(hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        NativeMethods.UnhookWindowsHookEx(hook);
    }
}

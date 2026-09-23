using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace TecladoFlotante
{
    internal static class Native
    {
        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        public const int WM_MOUSEACTIVATE = 0x0021;
        public const int MA_NOACTIVATE = 3;
        public const int WM_HOTKEY = 0x0312;
        public const int WM_DPICHANGED = 0x02E0;

        public static readonly IntPtr HWND_TOP = IntPtr.Zero;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;

        public const uint MOD_ALT = 0x1;
        public const uint MOD_CONTROL = 0x2;
        public const uint MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint count, INPUT[] inputs, int size);

        [DllImport("user32.dll")]
        public static extern uint MapVirtualKey(uint code, uint mapType);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern short VkKeyScanEx(char ch, IntPtr hkl);

        [DllImport("user32.dll")]
        public static extern IntPtr GetKeyboardLayout(uint threadId);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hWnd);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hWnd, int attr, ref int value, int size);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx, dy;
            public uint mouseData, dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk, wScan;
            public uint dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        public static float DpiScale(IntPtr hwnd)
        {
            try
            {
                uint dpi = GetDpiForWindow(hwnd);
                if (dpi > 0) return dpi / 96f;
            }
            catch (EntryPointNotFoundException) { }
            return 1f;
        }

        public static void RoundCorners(IntPtr hwnd, int preference, int borderColorBgr)
        {
            try
            {
                DwmSetWindowAttribute(hwnd, 33, ref preference, 4);   // DWMWA_WINDOW_CORNER_PREFERENCE
                DwmSetWindowAttribute(hwnd, 34, ref borderColorBgr, 4); // DWMWA_BORDER_COLOR
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
    }

    /// <summary>Envía pulsaciones al programa que tiene el foco.</summary>
    internal static class Input
    {
        const uint INPUT_KEYBOARD = 1;
        const uint KEYEVENTF_EXTENDEDKEY = 0x1;
        const uint KEYEVENTF_KEYUP = 0x2;
        const uint KEYEVENTF_UNICODE = 0x4;

        public const ushort VK_BACK = 0x08, VK_TAB = 0x09, VK_RETURN = 0x0D, VK_ESCAPE = 0x1B, VK_SPACE = 0x20;
        public const ushort VK_END = 0x23, VK_HOME = 0x24, VK_LEFT = 0x25, VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28;
        public const ushort VK_DELETE = 0x2E, VK_LWIN = 0x5B, VK_F1 = 0x70;
        public const ushort VK_LSHIFT = 0xA0, VK_LCONTROL = 0xA2, VK_LMENU = 0xA4;

        static bool IsExtended(ushort vk)
        {
            switch (vk)
            {
                case 0x21: case 0x22: case 0x23: case 0x24: case 0x25: case 0x26: case 0x27: case 0x28:
                case 0x2C: case 0x2D: case 0x2E: case 0x5B: case 0x5C: case 0x5D:
                case 0x6F: case 0x90: case 0xA3: case 0xA5:
                    return true;
            }
            return false;
        }

        static Native.INPUT VkEvent(ushort vk, bool up)
        {
            Native.INPUT i = new Native.INPUT();
            i.type = INPUT_KEYBOARD;
            i.U.ki.wVk = vk;
            i.U.ki.wScan = (ushort)Native.MapVirtualKey(vk, 0);
            i.U.ki.dwFlags = (up ? KEYEVENTF_KEYUP : 0) | (IsExtended(vk) ? KEYEVENTF_EXTENDEDKEY : 0);
            return i;
        }

        static Native.INPUT CharEvent(char c, bool up)
        {
            Native.INPUT i = new Native.INPUT();
            i.type = INPUT_KEYBOARD;
            i.U.ki.wScan = c;
            i.U.ki.dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0);
            return i;
        }

        static void Send(List<Native.INPUT> list)
        {
            if (list.Count == 0) return;
            Native.SendInput((uint)list.Count, list.ToArray(), System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.INPUT)));
        }

        public static void SendText(string text)
        {
            List<Native.INPUT> list = new List<Native.INPUT>();
            foreach (char c in text)
            {
                if (c == '\n') { list.Add(VkEvent(VK_RETURN, false)); list.Add(VkEvent(VK_RETURN, true)); continue; }
                list.Add(CharEvent(c, false));
                list.Add(CharEvent(c, true));
            }
            Send(list);
        }

        public static void SendVk(ushort vk, bool shift, bool ctrl, bool alt, bool win)
        {
            List<ushort> mods = new List<ushort>();
            if (ctrl) mods.Add(VK_LCONTROL);
            if (alt) mods.Add(VK_LMENU);
            if (shift) mods.Add(VK_LSHIFT);
            if (win) mods.Add(VK_LWIN);

            List<Native.INPUT> list = new List<Native.INPUT>();
            foreach (ushort m in mods) list.Add(VkEvent(m, false));
            list.Add(VkEvent(vk, false));
            list.Add(VkEvent(vk, true));
            for (int i = mods.Count - 1; i >= 0; i--) list.Add(VkEvent(mods[i], true));
            Send(list);
        }

        /// <summary>Atajos tipo Ctrl+C: hace falta la tecla virtual real, no el carácter Unicode.</summary>
        public static void SendCombo(char c, bool shift, bool ctrl, bool alt, bool win)
        {
            char up = char.ToUpperInvariant(c);
            if (up >= 'A' && up <= 'Z') { SendVk(up, shift, ctrl, alt, win); return; }
            if (c >= '0' && c <= '9') { SendVk(c, shift, ctrl, alt, win); return; }

            IntPtr hkl = Native.GetKeyboardLayout(Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), IntPtr.Zero));
            short r = Native.VkKeyScanEx(c, hkl);
            if (r == -1) { SendText(c.ToString()); return; }
            ushort vk = (ushort)(r & 0xFF);
            int st = (r >> 8) & 0xFF;
            SendVk(vk, shift || (st & 1) != 0, ctrl || (st & 2) != 0, alt || (st & 4) != 0, win);
        }
    }
}

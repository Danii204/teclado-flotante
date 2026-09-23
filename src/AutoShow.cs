using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;

namespace TecladoFlotante
{
    /// <summary>
    /// «Mostrar al tocar un campo de texto»: tras cada clic o toque fuera del teclado, comprueba si el
    /// elemento enfocado admite escritura y, si es así, pide mostrar el teclado.
    ///
    /// - El gancho de ratón (WH_MOUSE_LL) solo anota que hubo un clic; no lee ni guarda nada más.
    /// - La comprobación se hace en un hilo aparte (UI Automation puede tardar), nunca en el de la interfaz.
    /// - Editable = hay cursor de texto (caret) en la ventana activa, o UI Automation indica un campo de
    ///   edición no de solo lectura.
    /// </summary>
    public sealed class AutoShow : IDisposable
    {
        const int WH_MOUSE_LL = 14;
        const int WM_LBUTTONUP = 0x0202;

        delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int idHook, HookProc proc, IntPtr hMod, uint threadId);
        [DllImport("user32.dll")]
        static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")]
        static extern bool GetGUIThreadInfo(uint threadId, ref GUITHREADINFO info);

        [StructLayout(LayoutKind.Sequential)]
        struct GUITHREADINFO
        {
            public int cbSize, flags;
            public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
            public Native.RECT rcCaret;
        }

        readonly Control invoker;
        readonly Func<Point, bool> ignoreClick;   // true si el clic cae en el propio teclado o botón flotante
        readonly Action onEditableFocus;
        readonly int ownPid = Process.GetCurrentProcess().Id;
        readonly AutoResetEvent clicked = new AutoResetEvent(false);
        HookProc proc;
        IntPtr hook;
        Thread worker;
        volatile bool running;

        public AutoShow(Control invoker, Func<Point, bool> ignoreClick, Action onEditableFocus)
        {
            this.invoker = invoker;
            this.ignoreClick = ignoreClick;
            this.onEditableFocus = onEditableFocus;
        }

        public bool Enabled { get { return hook != IntPtr.Zero; } }

        /// <summary>Debe llamarse desde el hilo de la interfaz (el gancho usa su cola de mensajes).</summary>
        public void Start()
        {
            if (hook != IntPtr.Zero) return;
            proc = HookCallback;
            hook = SetWindowsHookEx(WH_MOUSE_LL, proc, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero)
            {
                Log.Error("No se pudo activar «mostrar al tocar un campo de texto» (error " + Marshal.GetLastWin32Error() + ")", null);
                return;
            }
            running = true;
            worker = new Thread(Worker) { IsBackground = true, Name = "Detección de campos de texto" };
            worker.Start();
        }

        public void Stop()
        {
            if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
            running = false;
            clicked.Set();
        }

        IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (code >= 0 && wParam == (IntPtr)WM_LBUTTONUP)
                {
                    Point p = new Point(Marshal.ReadInt32(lParam), Marshal.ReadInt32(lParam, 4));
                    if (!ignoreClick(p)) clicked.Set();
                }
            }
            catch { /* un gancho nunca debe fallar */ }
            return CallNextHookEx(hook, code, wParam, lParam);
        }

        void Worker()
        {
            while (true)
            {
                clicked.WaitOne();
                if (!running) return;
                Thread.Sleep(120); // dejar que el foco se asiente tras el clic
                clicked.Reset();   // agrupa ráfagas de clics en una sola comprobación
                try
                {
                    if (IsEditableFocused()) invoker.BeginInvoke(onEditableFocus);
                }
                catch (InvalidOperationException) { return; } // la ventana ya se cerró
                catch (Exception ex) { Log.Error("Fallo comprobando el campo enfocado", ex); }
            }
        }

        /// <summary>¿Admite escritura lo que tiene el foco en la ventana activa (de otro programa)?</summary>
        public bool IsEditableFocused()
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;
            uint pid;
            uint tid = GetWindowThreadProcessId(fg, out pid);
            if (pid == ownPid) return false;

            GUITHREADINFO gti = new GUITHREADINFO();
            gti.cbSize = Marshal.SizeOf(typeof(GUITHREADINFO));
            if (GetGUIThreadInfo(tid, ref gti) && gti.hwndCaret != IntPtr.Zero) return true;

            AutomationElement el = AutomationElement.FocusedElement;
            if (el == null) return false;
            AutomationElement.AutomationElementInformation info = el.Current;
            if (info.ProcessId == ownPid) return false;
            ControlType ct = info.ControlType;
            if (ct != ControlType.Edit && ct != ControlType.Document && ct != ControlType.ComboBox) return false;

            object vp;
            if (el.TryGetCurrentPattern(ValuePattern.Pattern, out vp))
                return !((ValuePattern)vp).Current.IsReadOnly;
            return ct == ControlType.Edit; // campos sin ValuePattern (p. ej. contraseñas en algunos programas)
        }

        public void Dispose()
        {
            Stop();
        }
    }
}

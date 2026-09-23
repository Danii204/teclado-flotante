using System;
using System.Collections.Generic;
using System.Drawing;

namespace TecladoFlotante
{
    public enum KeyKind { Char, Vk, Shift, Caps, Ctrl, Alt, AltGr, Win, NumLock }

    public class KeyDef
    {
        public string Id;
        public KeyKind Kind;
        public string Normal, Shifted, AltGr;   // teclas de carácter
        public ushort Vk;                        // teclas especiales
        public string Label;
        public bool Icon;                        // Label es un glifo de Segoe Fluent Icons
        public bool Repeat;
        public int Row, RowSpan = 1;
        public float X, W;
        public RectangleF Rect;
        public bool Numpad;                      // pertenece al bloque numérico
        public ushort NavVk;                     // con Bloq Num desactivado (7 = Inicio, 8 = ↑...)
        public string NavLabel;

        public bool IsLetter { get { return Kind == KeyKind.Char && char.IsLetter(Normal[0]); } }
        public bool IsSpecial { get { return Kind != KeyKind.Char && Vk != Input.VK_SPACE; } }
    }

    /// <summary>Una distribución completa: bloque principal + fila de funciones + bloque numérico.</summary>
    public class LayoutDef
    {
        public string Id, Name;
        public List<KeyDef> Keys;
        public HashSet<string> DeadKeys;   // ´ ` ^ ¨ en español; ninguna en inglés (EE. UU.)
    }

    /// <summary>
    /// Geometría en unidades de tecla: bloque principal de 15 de ancho, fila de funciones de 0,75 de alto
    /// (opcional, fila 0) y bloque numérico opcional de 4 de ancho a la derecha.
    /// </summary>
    public static class KeyLayout
    {
        public const float MainColumns = 15f;
        public const float NumpadGap = 0.3f;
        public const float NumpadColumns = 4f;
        public const float FnRowHeight = 0.75f;
        public const string DefaultId = "es";

        public static readonly string[] Ids = { "es", "us" };

        public static float Columns(bool numpad) { return numpad ? MainColumns + NumpadGap + NumpadColumns : MainColumns; }
        public static float Rows(bool fnRow) { return fnRow ? 5 + FnRowHeight : 5; }
        public static float RowTop(int row, bool fnRow) { return row == 0 ? 0 : (fnRow ? FnRowHeight : 0) + (row - 1); }
        public static float RowHeight(KeyDef k) { return k.Row == 0 ? FnRowHeight : k.RowSpan; }

        public static string NameOf(string id) { return id == "us" ? "English (US)" : "Español (España)"; }

        public static LayoutDef Build(string id)
        {
            return id == "us" ? English() : Spanish();
        }

        // ------------------------------------------------------------------ ayudantes de construcción

        class Builder
        {
            public readonly List<KeyDef> Keys = new List<KeyDef>();
            float x;
            int row;

            public void Row(int r) { row = r; x = 0; }

            public KeyDef Add(KeyDef k, float w)
            {
                k.Row = row; k.X = x; k.W = w; x += w;
                Keys.Add(k);
                return k;
            }

            public void Char(string normal, string shifted, string altGr)
            {
                Add(new KeyDef
                {
                    Id = normal, Kind = KeyKind.Char, Normal = normal,
                    Shifted = shifted ?? normal.ToUpperInvariant(), AltGr = altGr, Repeat = true,
                }, 1);
            }

            public void Letters(string s, Func<char, string> altGr)
            {
                foreach (char c in s) Char(c.ToString(), null, altGr == null ? null : altGr(c));
            }

            public KeyDef Vk(string id, string label, ushort vk, bool icon, bool repeat, float w)
            {
                return Add(new KeyDef { Id = id, Kind = KeyKind.Vk, Label = label, Vk = vk, Icon = icon, Repeat = repeat }, w);
            }

            public KeyDef Mod(string id, string label, KeyKind kind, bool icon, float w)
            {
                return Add(new KeyDef { Id = id, Kind = kind, Label = label, Icon = icon }, w);
            }
        }

        static void FunctionRow(Builder b)
        {
            b.Row(0);
            b.Vk("Esc", "Esc", Input.VK_ESCAPE, false, false, 1);
            for (int i = 1; i <= 12; i++) b.Vk("F" + i, "F" + i, (ushort)(Input.VK_F1 + i - 1), false, false, 1);
            b.Vk("Inicio", "Inicio", Input.VK_HOME, false, true, 1);
            b.Vk("Fin", "Fin", Input.VK_END, false, true, 1);
        }

        static void BottomRows(Builder b, float shiftL, Action<Builder> row4Keys, bool altGr)
        {
            b.Row(4);
            b.Mod("ShiftL", "", KeyKind.Shift, true, shiftL);
            row4Keys(b);
            b.Mod("ShiftR", "", KeyKind.Shift, true, 0.75f);
            b.Vk("Up", "", Input.VK_UP, true, true, 1);
            b.Vk("Supr", "Supr", Input.VK_DELETE, false, true, 1);

            b.Row(5);
            b.Mod("Ctrl", "Ctrl", KeyKind.Ctrl, false, 1.25f);
            b.Mod("Win", "", KeyKind.Win, false, 1);
            b.Mod("Alt", "Alt", KeyKind.Alt, false, 1.25f);
            b.Vk("Space", "", Input.VK_SPACE, false, true, 5.75f);
            if (altGr) b.Mod("AltGr", "Alt Gr", KeyKind.AltGr, false, 1.25f);
            else b.Mod("AltR", "Alt", KeyKind.Alt, false, 1.25f);
            b.Mod("CtrlR", "Ctrl", KeyKind.Ctrl, false, 1.5f);
            b.Vk("Left", "", Input.VK_LEFT, true, true, 1);
            b.Vk("Down", "", Input.VK_DOWN, true, true, 1);
            b.Vk("Right", "", Input.VK_RIGHT, true, true, 1);
        }

        static void EnterKey(Builder b)
        {
            b.Vk("Enter", "", Input.VK_RETURN, true, true, 1.25f).RowSpan = 2;
        }

        static void Numpad(List<KeyDef> keys)
        {
            Action<KeyDef, int, float, float, int> np = (k, r, col, w, span) =>
            {
                k.Numpad = true; k.Row = r; k.X = MainColumns + NumpadGap + col; k.W = w; k.RowSpan = span;
                keys.Add(k);
            };
            Func<string, string, ushort, bool, bool, KeyDef> vk = (id, label, code, icon, repeat) =>
                new KeyDef { Id = id, Kind = KeyKind.Vk, Label = label, Vk = code, Icon = icon, Repeat = repeat };
            Func<string, ushort, string, bool, KeyDef> num = (n, nav, navLabel, navIcon) =>
                new KeyDef { Id = "num" + n, Kind = KeyKind.Char, Normal = n, Shifted = n, Repeat = true, NavVk = nav, NavLabel = navLabel, Icon = navIcon };

            np(vk("Impr", "Impr", 0x2C, false, false), 0, 0, 1, 1);
            np(vk("Insert", "Insert", 0x2D, false, false), 0, 1, 1, 1);
            np(vk("RePag", "RePág", 0x21, false, true), 0, 2, 1, 1);
            np(vk("AvPag", "AvPág", 0x22, false, true), 0, 3, 1, 1);
            np(new KeyDef { Id = "NumLock", Kind = KeyKind.NumLock, Label = "Bloq Num" }, 1, 0, 1, 1);
            np(num("/", 0, null, false), 1, 1, 1, 1);
            np(num("*", 0, null, false), 1, 2, 1, 1);
            np(num("-", 0, null, false), 1, 3, 1, 1);
            np(num("7", Input.VK_HOME, "Inicio", false), 2, 0, 1, 1);
            np(num("8", Input.VK_UP, "", true), 2, 1, 1, 1);
            np(num("9", 0x21, "RePág", false), 2, 2, 1, 1);
            np(num("+", 0, null, false), 2, 3, 1, 2);
            np(num("4", Input.VK_LEFT, "", true), 3, 0, 1, 1);
            np(num("5", 0, "", false), 3, 1, 1, 1);
            np(num("6", Input.VK_RIGHT, "", true), 3, 2, 1, 1);
            np(num("1", Input.VK_END, "Fin", false), 4, 0, 1, 1);
            np(num("2", Input.VK_DOWN, "", true), 4, 1, 1, 1);
            np(num("3", 0x22, "AvPág", false), 4, 2, 1, 1);
            np(vk("numEnter", "", Input.VK_RETURN, true, true), 4, 3, 1, 2);
            np(num("0", 0x2D, "Insert", false), 5, 0, 2, 1);
            np(num(".", Input.VK_DELETE, "Supr", false), 5, 2, 1, 1);
        }

        // ------------------------------------------------------------------ distribuciones

        /// <summary>Español (España), ISO.</summary>
        static LayoutDef Spanish()
        {
            Builder b = new Builder();
            FunctionRow(b);

            b.Row(1);
            b.Char("º", "ª", "\\");
            b.Char("1", "!", "|");
            b.Char("2", "\"", "@");
            b.Char("3", "·", "#");
            b.Char("4", "$", "~");
            b.Char("5", "%", "€");
            b.Char("6", "&", "¬");
            b.Char("7", "/", null);
            b.Char("8", "(", null);
            b.Char("9", ")", null);
            b.Char("0", "=", null);
            b.Char("'", "?", null);
            b.Char("¡", "¿", null);
            b.Vk("Back", "", Input.VK_BACK, true, true, 2);

            Func<char, string> euro = c => c == 'e' ? "€" : null;
            b.Row(2);
            b.Vk("Tab", "Tab", Input.VK_TAB, false, true, 1.75f);
            b.Letters("qwertyuiop", euro);
            b.Char("`", "^", "[");
            b.Char("+", "*", "]");
            EnterKey(b);

            b.Row(3);
            b.Mod("Caps", "Bloq Mayús", KeyKind.Caps, false, 1.75f);
            b.Letters("asdfghjklñ", null);
            b.Char("´", "¨", "{");
            b.Char("ç", "Ç", "}");

            BottomRows(b, 1.25f, r =>
            {
                r.Char("<", ">", null);
                r.Letters("zxcvbnm", null);
                r.Char(",", ";", null);
                r.Char(".", ":", null);
                r.Char("-", "_", null);
            }, true);

            Numpad(b.Keys);
            return new LayoutDef
            {
                Id = "es", Name = NameOf("es"), Keys = b.Keys,
                DeadKeys = new HashSet<string> { "´", "`", "^", "¨" },
            };
        }

        /// <summary>English (US), ANSI con la geometría del bloque ISO (Intro de dos filas).</summary>
        static LayoutDef English()
        {
            Builder b = new Builder();
            FunctionRow(b);

            b.Row(1);
            b.Char("`", "~", null);
            b.Char("1", "!", null);
            b.Char("2", "@", null);
            b.Char("3", "#", null);
            b.Char("4", "$", null);
            b.Char("5", "%", null);
            b.Char("6", "^", null);
            b.Char("7", "&", null);
            b.Char("8", "*", null);
            b.Char("9", "(", null);
            b.Char("0", ")", null);
            b.Char("-", "_", null);
            b.Char("=", "+", null);
            b.Vk("Back", "", Input.VK_BACK, true, true, 2);

            b.Row(2);
            b.Vk("Tab", "Tab", Input.VK_TAB, false, true, 1.75f);
            b.Letters("qwertyuiop", null);
            b.Char("[", "{", null);
            b.Char("]", "}", null);
            EnterKey(b);

            b.Row(3);
            b.Mod("Caps", "Caps Lock", KeyKind.Caps, false, 1.75f);
            b.Letters("asdfghjkl", null);
            b.Char(";", ":", null);
            b.Char("'", "\"", null);
            b.Char("\\", "|", null);

            BottomRows(b, 2.25f, r =>
            {
                r.Letters("zxcvbnm", null);
                r.Char(",", "<", null);
                r.Char(".", ">", null);
                r.Char("/", "?", null);
            }, false);

            Numpad(b.Keys);
            return new LayoutDef { Id = "us", Name = NameOf("us"), Keys = b.Keys, DeadKeys = new HashSet<string>() };
        }
    }
}

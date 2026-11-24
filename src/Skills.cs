using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Globalization;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;

namespace SkillVoiceTimer
{
    public sealed class Skill
    {
        public string Name { get; set; }
        [XmlIgnore]
        public Keys Key { get; set; }

        // Older profiles use WinForms enum names. Some keyboard layouts report keys
        // that net48 has no name for; these round-trip as decimal virtual-key codes.
        [XmlElement("Key")]
        public string StoredKey
        {
            get { return Enum.GetName(typeof(Keys), Key) ?? ((int)Key).ToString(CultureInfo.InvariantCulture); }
            set { Key = (Keys)Enum.Parse(typeof(Keys), value); }
        }
        [DefaultValue(Keys.None)]
        public Keys Modifiers { get; set; }
        [DefaultValue(false)]
        public bool WithWindowsKey { get; set; }
        public Keys Binding { get { return Key | Modifiers | (WithWindowsKey ? KeyChoices.Win : Keys.None); } }
        public int Duration { get; set; }
        public int Lead { get; set; }

        public int DelaySeconds { get { return Duration - Lead; } }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Name) || Name.Length > 40)
                throw new InvalidDataException("请输入 1 到 40 个字符的技能名。");
            if (!KeyChoices.IsKeyboardKey(Key) || (Modifiers & ~KeyChoices.StandardModifiers) != 0 || !KeyChoices.CanBind(Binding))
                throw new InvalidDataException("请重新录入有效的键盘快捷键。");
            if (Duration < 1 || Duration > 86400 || Lead < 0 || Lead >= Duration)
                throw new InvalidDataException("持续时间应在 1 到 86400 秒之间，提前量应小于持续时间。");
        }
    }

    internal static class KeyChoices
    {
        // WinForms only defines modifier bits for Ctrl, Alt and Shift.
        public const Keys Win = (Keys)0x80000;
        public const Keys StandardModifiers = Keys.Control | Keys.Alt | Keys.Shift;
        public static readonly Keys[] SidedModifiers = {
            Keys.LControlKey, Keys.RControlKey, Keys.LMenu, Keys.RMenu, Keys.LShiftKey, Keys.RShiftKey, Keys.LWin, Keys.RWin
        };

        public static bool IsKeyboardKey(Keys key)
        {
            int code = (int)key;
            return key == Keys.Cancel || (code >= 0x08 && code < 0xFF && key != Keys.ProcessKey && key != Keys.Packet);
        }

        public static bool CanBind(Keys binding)
        {
            var key = binding & Keys.KeyCode;
            var modifiers = binding & Keys.Modifiers;
            if (!IsKeyboardKey(key) || (modifiers & ~(StandardModifiers | Win)) != 0)
                return false;
            var flag = ModifierFlag(key);
            return flag == Keys.None || binding == ModifierShortcut(modifiers | flag);
        }

        // A chord containing only modifiers has the same identity in either press order.
        public static Keys ModifierShortcut(Keys flags)
        {
            if ((flags & Keys.Shift) != 0) return (flags & ~Keys.Shift) | Keys.ShiftKey;
            if ((flags & Keys.Alt) != 0) return (flags & ~Keys.Alt) | Keys.Menu;
            if ((flags & Keys.Control) != 0) return (flags & ~Keys.Control) | Keys.ControlKey;
            return flags == Win ? Keys.LWin : Keys.None;
        }

        public static Keys ModifierFlag(Keys key)
        {
            switch (key)
            {
                case Keys.ControlKey: case Keys.LControlKey: case Keys.RControlKey: return Keys.Control;
                case Keys.Menu: case Keys.LMenu: case Keys.RMenu: return Keys.Alt;
                case Keys.ShiftKey: case Keys.LShiftKey: case Keys.RShiftKey: return Keys.Shift;
                case Keys.LWin: case Keys.RWin: return Win;
                default: return Keys.None;
            }
        }

        public static string Label(Keys binding)
        {
            var parts = new List<string>();
            if ((binding & Win) != 0) parts.Add("Win");
            if ((binding & Keys.Control) != 0) parts.Add("Ctrl");
            if ((binding & Keys.Alt) != 0) parts.Add("Alt");
            if ((binding & Keys.Shift) != 0) parts.Add("Shift");
            parts.Add(BaseLabel(binding & Keys.KeyCode));
            return string.Join(" + ", parts);
        }

        private static string BaseLabel(Keys key)
        {
            if ((int)key >= (int)Keys.D0 && (int)key <= (int)Keys.D9)
                return ((int)key - (int)Keys.D0).ToString();
            if ((int)key >= (int)Keys.NumPad0 && (int)key <= (int)Keys.NumPad9)
                return "小键盘 " + ((int)key - (int)Keys.NumPad0);
            switch (key)
            {
                case Keys.Return: return "Enter";
                case Keys.Back: return "Backspace";
                case Keys.Prior: return "Page Up";
                case Keys.Next: return "Page Down";
                case Keys.Snapshot: return "Print Screen";
                case Keys.Escape: return "Esc";
                case Keys.ControlKey: return "Ctrl";
                case Keys.Menu: return "Alt";
                case Keys.ShiftKey: return "Shift";
                case Keys.LWin: return "Win";
                case Keys.Space: return "空格";
                case Keys.Capital: return "Caps Lock";
                case Keys.OemMinus: return "-";
                case Keys.Oemplus: return "=";
                case Keys.OemOpenBrackets: return "[";
                case Keys.OemCloseBrackets: return "]";
                case Keys.OemPipe: return "\\";
                case Keys.OemSemicolon: return ";";
                case Keys.OemQuotes: return "'";
                case Keys.Oemcomma: return ",";
                case Keys.OemPeriod: return ".";
                case Keys.OemQuestion: return "/";
                case Keys.Oemtilde: return "~";
                default: return Enum.GetName(typeof(Keys), key) ?? "VK 0x" + ((int)key).ToString("X2");
            }
        }
    }

    internal sealed class SkillStore
    {
        private readonly string path;
        private readonly XmlSerializer serializer = new XmlSerializer(typeof(List<Skill>));

        public SkillStore(string path) { this.path = path; }

        public List<Skill> Load()
        {
            if (!File.Exists(path))
                return new List<Skill> { new Skill { Name = "护盾", Key = Keys.F1, Duration = 60, Lead = 5 } };
            using (var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit }))
            {
                var skills = (List<Skill>)serializer.Deserialize(reader);
                Validate(skills);
                return skills;
            }
        }

        private static void Validate(List<Skill> skills)
        {
            var used = new HashSet<Keys>();
            foreach (var skill in skills)
            {
                skill.Validate();
                if (!used.Add(skill.Binding))
                    throw new InvalidDataException("每个快捷键只能绑定一个技能。");
            }
        }

        public void Save(List<Skill> skills)
        {
            Validate(skills);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = File.Create(temp))
                    serializer.Serialize(stream, skills);
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}

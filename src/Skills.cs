using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;

namespace SkillVoiceTimer
{
    public sealed class Skill
    {
        public string Name { get; set; }
        public Keys Key { get; set; }
        public int Duration { get; set; }
        public int Lead { get; set; }

        public int DelaySeconds { get { return Duration - Lead; } }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Name) || Name.Length > 40)
                throw new InvalidDataException("请输入 1 到 40 个字符的技能名。");
            if (!KeyChoices.All.Contains(Key))
                throw new InvalidDataException("请选择一个支持的技能键。");
            if (Duration < 1 || Duration > 86400 || Lead < 0 || Lead >= Duration)
                throw new InvalidDataException("持续时间应在 1 到 86400 秒之间，提前量应小于持续时间。");
        }
    }

    internal static class KeyChoices
    {
        public static readonly Keys[] All =
            Range(Keys.F1, 12).Concat(Range(Keys.D0, 10)).Concat(Range(Keys.A, 26))
            .Concat(Range(Keys.NumPad0, 10)).Concat(new[] {
                Keys.Space, Keys.Tab, Keys.Capital, Keys.OemMinus, Keys.Oemplus,
                Keys.OemOpenBrackets, Keys.OemCloseBrackets, Keys.OemPipe,
                Keys.OemSemicolon, Keys.OemQuotes, Keys.Oemcomma, Keys.OemPeriod,
                Keys.OemQuestion, Keys.Oemtilde, Keys.Up, Keys.Down, Keys.Left, Keys.Right
            }).ToArray();

        private static IEnumerable<Keys> Range(Keys start, int count)
        {
            return Enumerable.Range((int)start, count).Select(i => (Keys)i);
        }

        public static string Label(Keys key)
        {
            if ((int)key >= (int)Keys.D0 && (int)key <= (int)Keys.D9)
                return ((int)key - (int)Keys.D0).ToString();
            if ((int)key >= (int)Keys.NumPad0 && (int)key <= (int)Keys.NumPad9)
                return "小键盘 " + ((int)key - (int)Keys.NumPad0);
            switch (key)
            {
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
                default: return key.ToString();
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
                if (!used.Add(skill.Key))
                    throw new InvalidDataException("每个按键只能绑定一个技能。");
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

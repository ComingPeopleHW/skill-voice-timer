using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Serialization;

namespace SkillVoiceTimer
{
    public sealed class SkillPreset
    {
        public string Name { get; set; }
        public List<Skill> Skills { get; set; } = new List<Skill>();
    }

    public sealed class TimerSettings
    {
        public int Volume { get; set; } = 100;
        public int ActiveIndex { get; set; }
        public List<SkillPreset> Presets { get; set; } = new List<SkillPreset>();
        [XmlIgnore]
        public SkillPreset Current { get { return Presets[ActiveIndex]; } }

        internal static TimerSettings FromSkills(List<Skill> skills)
        {
            var result = new TimerSettings();
            result.Presets.Add(new SkillPreset { Name = "默认", Skills = skills });
            return result;
        }

        // Stage edits before saving. Reuse the same skill objects so unchanged timers keep running.
        internal TimerSettings Draft()
        {
            return new TimerSettings { Volume = Volume, ActiveIndex = ActiveIndex,
                Presets = Presets.Select(p => new SkillPreset { Name = p.Name, Skills = p.Skills }).ToList() };
        }

        internal void Validate()
        {
            if (Volume < 0 || Volume > 100) throw new InvalidDataException("播报音量应在 0 到 100 之间。");
            if (ActiveIndex < 0 || ActiveIndex >= Presets.Count) throw new InvalidDataException("当前预设无效。");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var preset in Presets)
            {
                if (string.IsNullOrWhiteSpace(preset.Name) || preset.Name.Length > 40 || !names.Add(preset.Name.Trim()))
                    throw new InvalidDataException("预设名称须为 1 到 40 个字符，且不能重复。");
                var bindings = new HashSet<System.Windows.Forms.Keys>();
                foreach (var skill in preset.Skills)
                {
                    skill.Validate();
                    if (!bindings.Add(skill.Binding)) throw new InvalidDataException("一个预设内每个快捷键只能绑定一个技能。");
                }
            }
        }
    }

    internal sealed class SkillStore
    {
        private readonly string path;
        private readonly string legacyPath;
        private readonly XmlSerializer serializer = new XmlSerializer(typeof(TimerSettings));

        public SkillStore(string path, string legacyPath = null)
        {
            this.path = path;
            this.legacyPath = legacyPath;
        }

        public TimerSettings Load()
        {
            var readerSettings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit };
            TimerSettings settings;
            if (File.Exists(path))
            {
                using (var reader = XmlReader.Create(path, readerSettings))
                    settings = (TimerSettings)serializer.Deserialize(reader);
            }
            else if (legacyPath != null && File.Exists(legacyPath))
            {
                using (var reader = XmlReader.Create(legacyPath, readerSettings))
                {
                    var legacy = new XmlSerializer(typeof(List<Skill>));
                    settings = TimerSettings.FromSkills((List<Skill>)legacy.Deserialize(reader));
                }
            }
            else
            {
                settings = TimerSettings.FromSkills(new List<Skill> {
                    new Skill { Name = "护盾", Key = System.Windows.Forms.Keys.F1, Duration = 60, Lead = 5 }
                });
            }
            settings.Validate();
            return settings;
        }

        public void Save(TimerSettings settings)
        {
            settings.Validate();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = File.Create(temp)) serializer.Serialize(stream, settings);
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}

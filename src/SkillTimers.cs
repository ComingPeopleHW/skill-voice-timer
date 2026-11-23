using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace SkillVoiceTimer
{
    internal sealed class SkillTimers
    {
        private sealed class Cast
        {
            public Skill Skill;
            public long Due;
            public long Order;
            public bool Announced;
        }

        private readonly Dictionary<Keys, Skill> bindings = new Dictionary<Keys, Skill>();
        private readonly Dictionary<Keys, Cast> casts = new Dictionary<Keys, Cast>();
        private readonly HashSet<Keys> held = new HashSet<Keys>();
        private readonly HashSet<Keys> modifierKeysDown = new HashSet<Keys>();
        private readonly HashSet<Keys> boundPrimaryKeys = new HashSet<Keys>();
        private long sequence;

        // The editor preserves unchanged objects, so editing one skill leaves the others running.
        public void Configure(IEnumerable<Skill> skills)
        {
            bindings.Clear();
            boundPrimaryKeys.Clear();
            foreach (var skill in skills)
            {
                bindings.Add(skill.Binding, skill);
                boundPrimaryKeys.Add(skill.Key);
            }
            foreach (var entry in casts.ToList())
            {
                Skill current;
                if (!bindings.TryGetValue(entry.Key, out current) || current != entry.Value.Skill)
                    casts.Remove(entry.Key);
            }
        }

        public void KeyEvent(Keys key, bool released, bool acceptCast, long now)
        {
            if (KeyChoices.ModifierFlag(key) != Keys.None || key == Keys.LWin || key == Keys.RWin)
            {
                if (released) modifierKeysDown.Remove(key);
                else modifierKeysDown.Add(key);
                return;
            }
            if (released) { held.Remove(key); return; }
            // Also remember keys pressed in the editor until release, including a newly assigned key.
            if (!acceptCast)
            {
                if (KeyChoices.All.Contains(key)) held.Add(key);
                return;
            }
            if (!boundPrimaryKeys.Contains(key) || !held.Add(key)) return;
            if (modifierKeysDown.Contains(Keys.LWin) || modifierKeysDown.Contains(Keys.RWin)) return;
            var binding = key;
            foreach (var modifier in modifierKeysDown) binding |= KeyChoices.ModifierFlag(modifier);
            Skill skill;
            if (!bindings.TryGetValue(binding, out skill)) return;
            casts[binding] = new Cast { Skill = skill, Due = now + skill.DelaySeconds * 1000L, Order = ++sequence };
        }

        // Dequeue only when the speaker is idle. A recast replaces even an overdue, queued reminder.
        public Skill TakeDue(long now)
        {
            var next = casts.Values.Where(c => !c.Announced && c.Due <= now)
                .OrderBy(c => c.Due).ThenBy(c => c.Order).FirstOrDefault();
            if (next == null) return null;
            next.Announced = true;
            return next.Skill;
        }

        public string Status(Keys binding, long now)
        {
            Cast cast;
            if (!casts.TryGetValue(binding, out cast)) return "等待按键";
            if (cast.Announced) return "已提醒";
            if (now >= cast.Due) return "等待播报";
            return ((cast.Due - now + 999) / 1000) + " 秒后提醒";
        }

        public void Clear() { casts.Clear(); held.Clear(); modifierKeysDown.Clear(); }
    }
}

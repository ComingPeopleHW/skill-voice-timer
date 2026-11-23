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
        private long sequence;

        // The editor preserves unchanged objects, so editing one skill leaves the others running.
        public void Configure(IEnumerable<Skill> skills)
        {
            bindings.Clear();
            foreach (var skill in skills) bindings.Add(skill.Key, skill);
            foreach (var entry in casts.ToList())
            {
                Skill current;
                if (!bindings.TryGetValue(entry.Key, out current) || current != entry.Value.Skill)
                    casts.Remove(entry.Key);
            }
            held.RemoveWhere(key => !bindings.ContainsKey(key));
        }

        public void KeyEvent(Keys key, bool released, bool acceptCast, long now)
        {
            if (released) { held.Remove(key); return; }
            Skill skill;
            if (!bindings.TryGetValue(key, out skill) || !held.Add(key) || !acceptCast) return;
            casts[key] = new Cast { Skill = skill, Due = now + skill.DelaySeconds * 1000L, Order = ++sequence };
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

        public string Status(Keys key, long now)
        {
            Cast cast;
            if (!casts.TryGetValue(key, out cast)) return "等待按键";
            if (cast.Announced) return "已提醒";
            if (now >= cast.Due) return "等待播报";
            return ((cast.Due - now + 999) / 1000) + " 秒后提醒";
        }

        public void Clear() { casts.Clear(); held.Clear(); }
    }
}

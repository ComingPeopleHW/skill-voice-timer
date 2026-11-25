using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace SkillVoiceTimer
{
    internal sealed class SkillTimers
    {
        private enum ReminderState { Pending, Announced, Reserved }

        private sealed class Cast
        {
            public Skill Skill;
            public long Due;
            public long ReadyAt { get { return Due + Skill.Lead * 1000L; } }
            public long Order;
            public ReminderState State;
        }

        private readonly Dictionary<Keys, Skill> bindings = new Dictionary<Keys, Skill>();
        private readonly Dictionary<Guid, Cast> casts = new Dictionary<Guid, Cast>();
        private readonly HashSet<Keys> held = new HashSet<Keys>();
        private readonly HashSet<Keys> modifierKeysDown = new HashSet<Keys>();
        private readonly HashSet<Keys> boundPrimaryKeys = new HashSet<Keys>();
        private long sequence;
        private Keys pendingModifierShortcut;

        public bool HasEnabledBindings { get { return bindings.Count > 0; } }

        public void Configure(IEnumerable<Skill> skills, Skill edited = null)
        {
            bindings.Clear();
            boundPrimaryKeys.Clear();
            foreach (var skill in skills)
            {
                // Track physical releases for all configured keys so enabling a held key
                // cannot turn its next auto-repeat into a new cast.
                boundPrimaryKeys.Add(skill.Key);
                if (skill.Enabled) bindings.Add(skill.Binding, skill);
            }
            if (!bindings.ContainsKey(pendingModifierShortcut)) pendingModifierShortcut = Keys.None;
            foreach (var entry in casts.ToList())
            {
                Skill current;
                if (!bindings.TryGetValue(entry.Value.Skill.Binding, out current) || current.Id != entry.Key || entry.Key == edited?.Id)
                    casts.Remove(entry.Key);
                else entry.Value.Skill = current; // Deleting its partner only removes the link.
            }
        }

        public void SwitchPreset(IEnumerable<Skill> skills)
        {
            casts.Clear();
            pendingModifierShortcut = Keys.None;
            Configure(skills);
        }

        public void KeyEvent(Keys key, bool released, bool acceptCast, long now)
        {
            if (KeyChoices.ModifierFlag(key) != Keys.None)
            {
                if (released)
                {
                    if (!modifierKeysDown.Remove(key)) return;
                    if (acceptCast && pendingModifierShortcut != Keys.None) Start(pendingModifierShortcut, now);
                    pendingModifierShortcut = Keys.None;
                }
                else if (modifierKeysDown.Add(key))
                {
                    var shortcut = KeyChoices.ModifierShortcut(CurrentModifiers());
                    pendingModifierShortcut = acceptCast && bindings.ContainsKey(shortcut) ? shortcut : Keys.None;
                }
                return;
            }
            if (released)
            {
                if (!held.Remove(key) && key == Keys.PrintScreen && acceptCast)
                {
                    pendingModifierShortcut = Keys.None;
                    Start(key | CurrentModifiers(), now);
                }
                return;
            }
            pendingModifierShortcut = Keys.None;
            // Also remember keys pressed in the editor until release, including a newly assigned key.
            if (!acceptCast)
            {
                if (KeyChoices.IsKeyboardKey(key)) held.Add(key);
                return;
            }
            if (!boundPrimaryKeys.Contains(key) || !held.Add(key)) return;
            Start(key | CurrentModifiers(), now);
        }

        private Keys CurrentModifiers()
        {
            var flags = Keys.None;
            foreach (var modifier in modifierKeysDown) flags |= KeyChoices.ModifierFlag(modifier);
            return flags;
        }

        private void Start(Keys binding, long now)
        {
            Skill skill;
            if (!bindings.TryGetValue(binding, out skill)) return;
            Cast current;
            if (casts.TryGetValue(skill.Id, out current))
            {
                if (now < current.Due) return;
                // Settle due reminders against the old cooldown before replacing it.
                MarkReserved(now);
            }
            casts[skill.Id] = new Cast { Skill = skill, Due = now + skill.DelaySeconds * 1000L, Order = ++sequence };
        }

        private void MarkReserved(long now)
        {
            foreach (var cast in casts.Values)
            {
                var skill = cast.Skill;
                if (cast.State != ReminderState.Pending || cast.Due > now || !skill.ReserveForId.HasValue) continue;
                Cast partner;
                if (casts.TryGetValue(skill.ReserveForId.Value, out partner) &&
                    partner.ReadyAt - now <= (skill.Duration + skill.BufferSeconds) * 1000L)
                    cast.State = ReminderState.Reserved;
            }
        }

        public Skill TakeDue(long now, bool speakerAvailable = true)
        {
            MarkReserved(now);
            if (!speakerAvailable) return null;
            var next = casts.Values.Where(c => c.State == ReminderState.Pending && c.Due <= now)
                .OrderBy(c => c.Due).ThenBy(c => c.Order).FirstOrDefault();
            if (next == null) return null;
            next.State = ReminderState.Announced;
            return next.Skill;
        }

        public string Status(Keys binding, long now)
        {
            Skill skill;
            if (!bindings.TryGetValue(binding, out skill)) return "未勾选";
            Cast cast;
            if (!casts.TryGetValue(skill.Id, out cast)) return "等待按键";
            if (cast.State == ReminderState.Reserved) return "本轮已留用";
            if (cast.State == ReminderState.Announced) return "已提醒";
            if (now >= cast.Due) return "等待播报";
            return ((cast.Due - now + 999) / 1000) + " 秒后提醒";
        }

        public void Clear() { casts.Clear(); held.Clear(); modifierKeysDown.Clear(); pendingModifierShortcut = Keys.None; }
    }
}

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using SkillVoiceTimer;

internal static partial class Checks
{
    private static void CheckLinkedTimers()
    {
        var b = new Skill { Name = "B", Key = Keys.B, Duration = 120, Lead = 10 };
        var a = new Skill { Name = "A", Key = Keys.A, Duration = 30, ReserveForId = b.Id };
        var timers = new SkillTimers();
        timers.Configure(new[] { a, b });
        Press(timers, a.Key, 0);
        Check(timers.TakeDue(30000) == a, "A speaks normally before the first observed B press");

        timers.Clear();
        Press(timers, a.Key, 0);
        Press(timers, b.Key, 0);
        Check(timers.TakeDue(30000) == a, "A speaks while B has 90 seconds remaining");
        Press(timers, a.Key, 30000);
        Check(timers.TakeDue(60000) == a, "A speaks while B has 60 seconds remaining");
        Press(timers, a.Key, 60000);
        Check(timers.TakeDue(90000) == null && timers.Status(a.Binding, 90000) == "本轮已留用",
            "A is held for B at 90 seconds with no new countdown");
        Check(timers.TakeDue(109999) == null && timers.TakeDue(110000) == b && timers.TakeDue(120000) == null,
            "B speaks its own name once at its own early reminder");
        Press(timers, a.Key, 121000);
        Check(timers.TakeDue(151000) == null, "A is also reserved when B is already ready");

        var d = new Skill { Name = "D", Key = Keys.D, Duration = 130, Lead = 20 };
        var c = new Skill { Name = "C", Key = Keys.C, Duration = 30, ReserveForId = d.Id, BufferSeconds = 0 };
        timers.Configure(new[] { a, b, c, d });
        timers.Clear();
        Press(timers, b.Key, 0);
        Press(timers, d.Key, 0);
        Press(timers, a.Key, 57000);
        Press(timers, c.Key, 57000);
        Check(timers.TakeDue(87000) == c && timers.TakeDue(87000) == null &&
            timers.Status(a.Binding, 87000) == "本轮已留用", "33 second boundary is inclusive and independent across pairs");
        timers.Clear();
        Press(timers, b.Key, 0);
        Press(timers, a.Key, 56999);
        Check(timers.TakeDue(86999) == a, "33 seconds plus one millisecond allows A using B's full cooldown");

        timers.Clear();
        a.BufferSeconds = 0;
        Press(timers, b.Key, 0);
        Press(timers, a.Key, 57000);
        Check(timers.TakeDue(87000) == a, "zero buffer narrows A's hold window");
        a.BufferSeconds = 3;
        timers.Clear();
        Press(timers, b.Key, 0);
        Press(timers, a.Key, 57000);
        timers.TakeDue(87000, false);
        Press(timers, b.Key, 120000);
        Check(timers.TakeDue(120001) == null && timers.Status(a.Binding, 120001) == "本轮已留用",
            "a busy speaker and B's next cast cannot revive a skipped A");
        Press(timers, a.Key, 120002);
        Check(timers.TakeDue(150002) == a, "only a fresh A press starts its next reminder");

        timers.Clear();
        Press(timers, b.Key, 0);
        Press(timers, a.Key, 57000);
        b.Enabled = false;
        timers.Configure(new[] { a, b });
        Check(timers.TakeDue(87000) == a, "unchecking B lets A speak independently");
        b.Enabled = true;
        timers.Configure(new[] { a, b });
        Press(timers, b.Key, 90000);
        Press(timers, a.Key, 150000);
        timers.SwitchPreset(new[] { a.Copy(), b.Copy() });
        Press(timers, a.Key, 150001);
        Check(timers.TakeDue(180001)?.Id == a.Id, "switching presets forgets B's previous cooldown even with copied IDs");
    }

    private static void CheckLinkEditor(MainWindow window, string temp)
    {
        var b = new Skill { Name = "爆发", Key = Keys.B, Duration = 120 };
        var a = new Skill { Name = "增益", Key = Keys.A, Duration = 30 };
        var skills = new List<Skill> { a, b };
        using (var editor = new SkillEditor(skills, a))
        {
            editor.Show(window);
            var fields = ((TableLayoutPanel)editor.Controls[0]).Controls;
            var target = fields.OfType<ComboBox>().Single();
            var buffer = fields.OfType<NumericUpDown>().Single(field => field.AccessibleName == "操作余量（秒）");
            Check(target.Items.Count == 2 && target.SelectedIndex == 0 && !buffer.Enabled && buffer.Value == 3,
                "editor offers other skills in the preset with a three-second default");
            target.SelectedIndex = 1;
            Check(buffer.Enabled && fields.OfType<Label>().Any(label => label.Text.Contains("33 秒")), "editor previews the total hold window");
            CapturePreview(editor, "SKILL_TIMER_LINK_PREVIEW");
            buffer.Value = 7;
            ((Button)editor.AcceptButton).PerformClick();
            skills[0] = editor.Result;
            Check(skills[0].ReserveForId == b.Id && skills[0].Id == a.Id && skills[0].BufferSeconds == 7,
                "editor saves stable partner and custom buffer");
        }
        var settings = TimerSettings.FromSkills(skills);
        settings.Presets.Add(new SkillPreset { Name = "复制", Skills = skills.Select(skill => skill.Copy()).ToList() });
        settings.Current.Skills[1].Name = "改名";
        settings.Current.Skills[1].Key = Keys.End;
        var store = new SkillStore(Path.Combine(temp, "linked.xml"));
        store.Save(settings);
        settings = store.Load();
        foreach (var preset in settings.Presets)
            Check(preset.Skills[0].ReserveForId == preset.Skills[1].Id && preset.Skills[0].BufferSeconds == 7,
                "saved relation resolves locally after rename, rebind, copy and restart");
        using (var editor = new SkillEditor(settings.Current.Skills, settings.Current.Skills[0]))
        {
            editor.Show(window);
            var target = ((TableLayoutPanel)editor.Controls[0]).Controls.OfType<ComboBox>().Single();
            Check(target.SelectedItem.ToString().Contains("改名（End）"), "editor restores the renamed and rebound partner");
            target.SelectedIndex = 0;
            ((Button)editor.AcceptButton).PerformClick();
            Check(editor.Result.ReserveForId == null, "selecting no partner removes the relation");
        }
        var timers = new SkillTimers();
        timers.Configure(settings.Current.Skills);
        Press(timers, Keys.A, 0);
        var remaining = settings.Current.Without(settings.Current.Skills[1]);
        timers.Configure(remaining);
        Check(timers.TakeDue(30000) == remaining[0] && remaining[0].ReserveForId == null,
            "removing B clears the relation and preserves A's in-flight countdown");
        settings.Current.Skills = remaining;
        store.Save(settings);
        Check(store.Load().Presets[1].Skills[0].ReserveForId == b.Id, "removing B leaves links in copied presets intact");
    }
}

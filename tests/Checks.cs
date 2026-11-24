using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SkillVoiceTimer;

internal static partial class Checks
{
    private static int checks;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAILED: " + name);
        Console.WriteLine("PASS " + name);
        checks++;
    }

    private static void Press(SkillTimers runtime, Keys key, long now)
    {
        runtime.KeyEvent(key, false, true, now);
        runtime.KeyEvent(key, true, true, now);
    }

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        var a = new Skill { Name = "护盾", Key = Keys.F1, Duration = 60, Lead = 5 };
        var b = new Skill { Name = "陷阱", Key = Keys.D2, Duration = 120, Lead = 10 };
        var runtime = new SkillTimers();
        runtime.Configure(new[] { a, b });
        Press(runtime, Keys.Q, 0);
        Check(runtime.TakeDue(1000000) == null, "unbound key ignored");

        runtime.KeyEvent(a.Key, false, true, 0);
        runtime.KeyEvent(a.Key, false, true, 2000);
        Check(runtime.TakeDue(54999) == null, "no early announcement");
        Check(runtime.TakeDue(55000) == a, "60 minus 5 and held key repeat");
        Check(runtime.TakeDue(999999) == null, "one reminder per cast");
        runtime.KeyEvent(a.Key, true, true, 56000);
        Press(runtime, a.Key, 60000);
        Press(runtime, a.Key, 100000);
        Check(runtime.TakeDue(115000) == null, "recast invalidates original deadline");
        Check(runtime.TakeDue(155000) == a, "new deadline is relative to recast");

        Press(runtime, b.Key, 200000);
        Press(runtime, a.Key, 255000);
        Check(runtime.TakeDue(310000) == b, "oldest cast first at matching deadlines");
        Press(runtime, a.Key, 310010);
        Check(runtime.TakeDue(310100) == null, "recast replaces queued reminder");
        Check(runtime.TakeDue(365010) == a, "queued recast has a fresh deadline");

        Press(runtime, b.Key, 400000);
        Press(runtime, a.Key, 455000);
        Check(runtime.TakeDue(510000) == b && runtime.TakeDue(510001) == a &&
            runtime.TakeDue(510100) == null, "independent one-shot dispatch in sequence");
        Press(runtime, b.Key, 600000);
        Press(runtime, a.Key, 600000);
        runtime.Configure(new[] { b });
        Check(runtime.TakeDue(655000) == null && runtime.TakeDue(710000) == b, "remove one preserves other timers");
        runtime.KeyEvent(b.Key, false, false, 800000);
        runtime.KeyEvent(b.Key, false, true, 810000);
        runtime.KeyEvent(b.Key, true, true, 820000);
        Check(runtime.TakeDue(999999) == null, "editor input and its repeats ignored");
        Press(runtime, b.Key, 1000000);
        runtime.Clear();
        Check(runtime.TakeDue(1200000) == null, "pause clears pending reminders");

        var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var store = new SkillStore(Path.Combine(temp, "presets.xml"));
            Check(store.Load().Current.Skills.Single().DelaySeconds == 55, "new profile example");
            store.Save(TimerSettings.FromSkills(new List<Skill> { a, b }));
            var config = store.Load();
            var loaded = config.Current.Skills;
            Check(loaded.Count == 2 && loaded[1].Name == b.Name && loaded[1].DelaySeconds == 110 &&
                loaded[1].Key == b.Key && loaded[1].Modifiers == Keys.None, "Chinese profile roundtrip");
            bool rejected = false;
            try { store.Save(TimerSettings.FromSkills(new List<Skill> { a, a })); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && store.Load().Current.Skills.Count == 2, "duplicate binding leaves saved profile intact");
            rejected = false;
            try { new Skill { Name = "x", Key = Keys.Q, Duration = 5, Lead = 5 }.Validate(); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "invalid lead rejected");
            var instant = new Skill { Name = "x", Key = Keys.Q, Duration = 1, Lead = 0 };
            instant.Validate();
            Check(instant.DelaySeconds == 1, "zero lead allowed");
            store.Save(TimerSettings.FromSkills(new List<Skill>()));
            Check(store.Load().Current.Skills.Count == 0, "empty profile persists");
            CheckShortcuts(store);
            CheckSelectionTimers();
            Check(Marshal.SizeOf(typeof(KeyboardInput.Header)) == (IntPtr.Size == 8 ? 24 : 16) &&
                Marshal.SizeOf(typeof(KeyboardInput.KeyboardPacket)) == (IntPtr.Size == 8 ? 40 : 32), "Win32 keyboard ABI");
            using (var window = new MainWindow(store, config))
            {
                window.Show();
                Application.DoEvents();
                KeyboardInput.Register(window.Handle, true);
                KeyboardInput.Register(window.Handle, false);
                Check(window.Visible, "Windows main window and background input registration");
                CheckSelectionUi(window, store, loaded);
                CheckEditor(window, loaded);
                CheckExpandedKeys(temp, window);
                CheckPresets(window, store);
                window.Close();
            }
            CheckVolumeOutput();
        }
        finally { Directory.Delete(temp, true); }
        Console.WriteLine(checks + " checks passed.");
    }
}

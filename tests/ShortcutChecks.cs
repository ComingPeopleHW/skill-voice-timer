using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SkillVoiceTimer;

internal static partial class Checks
{
    private static Skill Bound(string name, Keys binding)
    {
        return new Skill { Name = name, Key = binding & Keys.KeyCode, Modifiers = binding & KeyChoices.StandardModifiers,
            WithWindowsKey = (binding & KeyChoices.Win) != 0,
            Duration = 60, Lead = 5 };
    }

    private static void CheckShortcuts(SkillStore store)
    {
        var plain = Bound("护盾", Keys.Q);
        var ctrl = Bound("陷阱", Keys.Control | Keys.Q);
        var all = Bound("结界", Keys.Control | Keys.Alt | Keys.Shift | Keys.Q);
        var shifted = Bound("疾跑", Keys.Shift | Keys.D1);
        var skills = new List<Skill> { plain, ctrl, all, shifted };
        store.Save(TimerSettings.FromSkills(skills));
        Check(store.Load().Current.Skills.Select(s => s.Binding).SequenceEqual(skills.Select(s => s.Binding)),
            "plain and multi-modifier shortcuts roundtrip together");
        Check(KeyChoices.Label(all.Binding) == "Ctrl + Alt + Shift + Q" &&
            KeyChoices.Label(shifted.Binding) == "Shift + 1", "readable shortcut labels");

        var timers = new SkillTimers();
        timers.Configure(skills);
        Press(timers, Keys.Q, 0);
        timers.KeyEvent(Keys.LControlKey, false, true, 1000);
        timers.KeyEvent(Keys.RControlKey, false, true, 1001);
        timers.KeyEvent(Keys.LControlKey, true, true, 1002);
        timers.KeyEvent(Keys.Q, false, true, 2000);
        timers.KeyEvent(Keys.RControlKey, true, true, 2001);
        timers.KeyEvent(Keys.Q, false, true, 2010);
        timers.KeyEvent(Keys.Q, true, true, 2011);
        Check(timers.TakeDue(55000) == plain && timers.TakeDue(57000) == ctrl && timers.TakeDue(59000) == null,
            "exact independent bindings, either Ctrl side, no repeat after modifier release");

        timers.KeyEvent(Keys.LMenu, false, true, 60000);
        Press(timers, Keys.Q, 60001);
        Check(timers.TakeDue(115001) == null, "unbound Alt chord does not trigger plain Q");
        timers.KeyEvent(Keys.LControlKey, false, true, 120000);
        timers.KeyEvent(Keys.RShiftKey, false, true, 120001);
        Press(timers, Keys.Q, 120002);
        Check(timers.TakeDue(175002) == all, "three held modifiers match a single skill");
        timers.Clear();
        Press(timers, Keys.Q, 180000);
        Check(timers.TakeDue(235000) == plain, "pause clears held modifiers");

        timers.KeyEvent(Keys.LWin, false, true, 250000);
        Press(timers, Keys.Q, 250001);
        Check(timers.TakeDue(305001) == null, "Windows chord cannot degrade into plain Q");
        timers.Clear();
        timers.KeyEvent(Keys.RShiftKey, false, true, 400000);
        Press(timers, Keys.D1, 400001);
        Check(timers.TakeDue(455001) == shifted, "Shift plus number uses the physical number key");
        timers.Clear();
        timers.KeyEvent(Keys.E, false, false, 500000);
        var newBinding = Bound("新技能", Keys.E);
        timers.Configure(new[] { newBinding });
        timers.KeyEvent(Keys.E, false, true, 500001);
        Check(timers.TakeDue(555001) == null, "held recording key cannot cast newly saved skill");
        timers.KeyEvent(Keys.E, true, true, 555010);
        Press(timers, Keys.E, 560000);
        Check(timers.TakeDue(615000) == newBinding, "new shortcut fires after release and fresh press");
        Check(KeyboardInput.PhysicalKey(new KeyboardInput.KeyboardPacket { VirtualKey = (ushort)Keys.Menu, Flags = 3 }) == Keys.RMenu &&
              KeyboardInput.PhysicalKey(new KeyboardInput.KeyboardPacket { VirtualKey = (ushort)Keys.ControlKey }) == Keys.LControlKey &&
              KeyboardInput.PhysicalKey(new KeyboardInput.KeyboardPacket { VirtualKey = (ushort)Keys.ShiftKey, MakeCode = 0x36 }) == Keys.RShiftKey,
            "raw keyboard distinguishes modifier sides");
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    private static void CheckExpandedKeys(string folder, MainWindow window)
    {
        var path = Path.Combine(folder, "keyboard.xml");
        var store = new SkillStore(Path.Combine(folder, "keyboard-presets.xml"), path);
        File.WriteAllText(path, "<ArrayOfSkill><Skill><Name>旧技能</Name><Key>F1</Key>" +
            "<Modifiers>Shift Control</Modifiers><Duration>60</Duration><Lead>5</Lead></Skill></ArrayOfSkill>");
        var originalXml = File.ReadAllText(path);
        var migration = store.Load();
        var legacy = migration.Current.Skills.Single();
        Check(legacy.Binding == (Keys.F1 | Keys.Shift | Keys.Control) && legacy.Enabled, "v0.2 named XML bindings load enabled");

        File.WriteAllText(path, originalXml.Replace("<Duration>", "<Enabled>false</Enabled><Duration>"));
        var disabledLegacy = store.Load();
        Check(!disabledLegacy.Current.Skills.Single().Enabled && disabledLegacy.Current.Name == "默认" &&
            disabledLegacy.Volume == 100, "v0.4 unchecked state migrates into the default preset");
        string oldXml = File.ReadAllText(path);
        store.Save(disabledLegacy);
        Check(File.ReadAllText(path) == oldXml, "saving presets preserves the original v0.4 file");

        var inputs = new[] { Keys.Home, Keys.End, Keys.Control | Keys.Home, Keys.Shift | Keys.End,
            Keys.PageUp, Keys.PageDown, Keys.Insert, Keys.Delete, Keys.Enter, Keys.Back, Keys.Add,
            Keys.F24, Keys.MediaPlayPause, Keys.Control | (Keys)0xC1, KeyChoices.Win | Keys.F15 };
        var recorded = new List<Skill>();
        foreach (var binding in inputs)
        {
            using (var editor = new SkillEditor(recorded, null))
            {
                editor.Show(window);
                var grid = (TableLayoutPanel)editor.Controls[0];
                grid.Controls.OfType<TextBox>().Single().Text = KeyChoices.Label(binding);
                var button = grid.Controls.OfType<ShortcutButton>().Single();
                button.PerformClick();
                Check(KeyDown(button, binding) && !button.Recording && button.Binding == binding,
                    "editor records " + KeyChoices.Label(binding));
                ((Button)editor.AcceptButton).PerformClick();
                if (editor.Result == null || editor.Result.Binding != binding) throw new Exception("Editor save lost binding");
                recorded.Add(editor.Result);
                editor.Close();
            }
        }
        store.Save(TimerSettings.FromSkills(recorded));
        var restored = store.Load().Current.Skills;
        Check(restored.Select(skill => skill.Binding).SequenceEqual(inputs), "expanded and unnamed VK bindings survive restart");
        foreach (var skill in restored)
        {
            var timers = new SkillTimers();
            timers.Configure(restored);
            foreach (var key in new[] { Keys.LControlKey, Keys.LShiftKey, Keys.LWin })
                if ((skill.Binding & KeyChoices.ModifierFlag(key)) != 0) timers.KeyEvent(key, false, true, 0);
            var primary = KeyboardInput.PhysicalKey(new KeyboardInput.KeyboardPacket { VirtualKey = (ushort)skill.Key });
            Press(timers, primary, 0);
            Check(timers.TakeDue(54999) == null && timers.TakeDue(55000) == skill && timers.TakeDue(56000) == null,
                "background timer matches only " + KeyChoices.Label(skill.Binding));
        }
        Check(KeyChoices.Label((Keys)0xC1) == "VK 0xC1" && KeyChoices.Label(Keys.PageDown) == "Page Down",
            "unknown and navigation key labels");
        Check(!KeyChoices.CanBind(Keys.None) && !KeyChoices.CanBind(Keys.LButton) && !KeyChoices.CanBind((Keys)0xFF)
            && !KeyChoices.CanBind(Keys.Packet) && !KeyChoices.CanBind(Keys.ProcessKey),
            "no bindable empty, mouse or placeholder keys");

        var single = Bound("单按", Keys.ControlKey);
        var chord = Bound("组合", Keys.Control | Keys.Home);
        var modifiers = Bound("双修饰键", Keys.Control | Keys.ShiftKey);
        var runtime = new SkillTimers();
        runtime.Configure(new[] { single, chord, modifiers });
        runtime.KeyEvent(Keys.RControlKey, false, true, 0);
        Check(runtime.TakeDue(60000) == null, "modifier-only shortcut waits for release");
        runtime.KeyEvent(Keys.RControlKey, true, true, 61000);
        Check(runtime.TakeDue(116000) == single, "right Ctrl tap fires standalone Ctrl");
        runtime.KeyEvent(Keys.LControlKey, false, true, 120000);
        Press(runtime, Keys.Home, 120010);
        runtime.KeyEvent(Keys.LControlKey, true, true, 120020);
        Check(runtime.TakeDue(175020) == chord && runtime.TakeDue(175021) == null, "Ctrl + Home never also fires standalone Ctrl");
        runtime.KeyEvent(Keys.LControlKey, false, true, 180000);
        Press(runtime, Keys.X, 180010);
        runtime.KeyEvent(Keys.LControlKey, true, true, 180020);
        Check(runtime.TakeDue(235020) == null, "unbound chord suppresses standalone modifier too");
        runtime.KeyEvent(Keys.RShiftKey, false, true, 240000);
        runtime.KeyEvent(Keys.LControlKey, false, true, 240010);
        runtime.KeyEvent(Keys.RShiftKey, true, true, 240020);
        runtime.KeyEvent(Keys.LControlKey, true, true, 240030);
        Check(runtime.TakeDue(295020) == modifiers && runtime.TakeDue(295030) == null,
            "modifier-only chord resolves once independent of order");
    }

    [DllImport("user32.dll")]
    private static extern bool GetKeyboardState([Out] byte[] state);
    [DllImport("user32.dll")]
    private static extern bool SetKeyboardState(byte[] state);

    // Exercise WinForms preprocessing with this test thread's modifier state.
    private static bool KeyDown(ShortcutButton button, Keys binding, bool repeat = false)
    {
        var original = new byte[256];
        if (!GetKeyboardState(original)) throw new Exception("Cannot read test thread keyboard state.");
        var state = (byte[])original.Clone();
        state[(int)Keys.ControlKey] = (byte)((binding & Keys.Control) == 0 ? 0 : 128);
        state[(int)Keys.Menu] = (byte)((binding & Keys.Alt) == 0 ? 0 : 128);
        state[(int)Keys.ShiftKey] = (byte)((binding & Keys.Shift) == 0 ? 0 : 128);
        state[(int)Keys.LWin] = (byte)((binding & KeyChoices.Win) == 0 ? 0 : 128);
        if (!SetKeyboardState(state)) throw new Exception("Cannot set test thread keyboard state.");
        try
        {
            int message = (binding & Keys.Alt) == 0 ? 0x0100 : 0x0104;
            var input = Message.Create(button.Handle, message, (IntPtr)(int)(binding & Keys.KeyCode),
                (IntPtr)(repeat ? 0x40000001 : 1));
            return button.PreProcessMessage(ref input);
        }
        finally { SetKeyboardState(original); }
    }

    private static void CheckEditor(MainWindow window, List<Skill> skills)
    {
        using (var editor = new SkillEditor(skills, skills[0]))
        {
            editor.Show(window);
            Application.DoEvents();
            var grid = (TableLayoutPanel)editor.Controls[0];
            var button = grid.Controls.OfType<ShortcutButton>().Single();
            var save = (Button)editor.AcceptButton;
            Check(button.Binding == Keys.F1 && save.Enabled, "existing single-key profile opens in editor");
            button.PerformClick();
            Check(button.Recording && !save.Enabled, "click arms recording and waits for a key");
            KeyDown(button, Keys.Control | Keys.ControlKey);
            Check(button.Recording && button.Binding == Keys.F1, "bare Ctrl waits for primary key");
            SendMessage(button.Handle, 0x0101, (IntPtr)(int)Keys.ControlKey, IntPtr.Zero);
            Check(!button.Recording && button.Binding == Keys.ControlKey, "editor records standalone Ctrl on release");
            button.PerformClick();
            KeyDown(button, Keys.F1);
            button.PerformClick();
            KeyDown(button, Keys.D2);
            Check(button.Recording && !save.Enabled && button.Hint.Contains(skills[1].Name),
                "duplicate capture names the other skill and waits for a new key");
            KeyDown(button, Keys.Escape);
            SendMessage(button.Handle, 0x0101, (IntPtr)(int)Keys.Escape, IntPtr.Zero);
            Check(!button.Recording && button.Binding == Keys.F1 && editor.Visible,
                "tapping Escape cancels capture and preserves previous binding");
            button.PerformClick();
            KeyDown(button, Keys.Escape);
            System.Threading.Thread.Sleep(700);
            SendMessage(button.Handle, 0x0101, (IntPtr)(int)Keys.Escape, IntPtr.Zero);
            Check(!button.Recording && button.Binding == Keys.Escape, "holding Escape records it as a skill key");
            button.PerformClick();
            Check(KeyDown(button, Keys.Tab) && button.Binding == Keys.Tab, "Tab is captured before dialog navigation");
            button.PerformClick();
            Check(KeyDown(button, Keys.Space) && button.Binding == Keys.Space &&
                KeyDown(button, Keys.Space, true) && !button.Recording, "space and autorepeat do not reopen recorder");
            button.PerformClick();
            Check(KeyDown(button, Keys.Alt | Keys.Shift | Keys.D1) &&
                button.Binding == (Keys.Alt | Keys.Shift | Keys.D1), "system key message records Alt + Shift + 1");
            button.PerformClick();
            grid.Controls.OfType<TextBox>().Single().Focus();
            Check(!button.Recording, "moving to another field ends capture");
            button.PerformClick();
            Check(KeyDown(button, Keys.Control | Keys.Q) && save.Enabled && !button.Recording &&
                button.Binding == (Keys.Control | Keys.Q) && skills[0].Binding == Keys.F1,
                "Ctrl + Q is staged without changing saved skill");
            var preview = Environment.GetEnvironmentVariable("SKILL_TIMER_EDITOR_PREVIEW");
            if (!string.IsNullOrEmpty(preview))
            {
                using (var bitmap = new Bitmap(editor.Width, editor.Height))
                {
                    editor.DrawToBitmap(bitmap, new Rectangle(Point.Empty, editor.Size));
                    bitmap.Save(preview, ImageFormat.Png);
                }
            }
            save.PerformClick();
            Check(editor.Result.Name == skills[0].Name && editor.Result.Key == Keys.Q &&
                  editor.Result.Modifiers == Keys.Control, "save commits exact captured shortcut");
            editor.Close();
        }
        using (var editor = new SkillEditor(skills, null))
        {
            editor.Show(window);
            var grid = (TableLayoutPanel)editor.Controls[0];
            grid.Controls.OfType<TextBox>().Single().Text = "新技能";
            Check(!((Button)editor.AcceptButton).Enabled, "new skill requires an explicitly recorded shortcut");
            editor.Close();
        }
    }
}

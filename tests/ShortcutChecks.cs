using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SkillVoiceTimer;

internal static partial class Checks
{
    private static Skill Bound(string name, Keys binding)
    {
        return new Skill { Name = name, Key = binding & Keys.KeyCode, Modifiers = binding & Keys.Modifiers,
            Duration = 60, Lead = 5 };
    }

    private static void CheckShortcuts(SkillStore store)
    {
        var plain = Bound("护盾", Keys.Q);
        var ctrl = Bound("陷阱", Keys.Control | Keys.Q);
        var all = Bound("结界", Keys.Control | Keys.Alt | Keys.Shift | Keys.Q);
        var shifted = Bound("疾跑", Keys.Shift | Keys.D1);
        var skills = new List<Skill> { plain, ctrl, all, shifted };
        store.Save(skills);
        Check(store.Load().Select(s => s.Binding).SequenceEqual(skills.Select(s => s.Binding)),
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
            KeyDown(button, Keys.D2);
            Check(button.Recording && !save.Enabled && button.Hint.Contains(skills[1].Name),
                "duplicate capture names the other skill and waits for a new key");
            Check(KeyDown(button, Keys.Escape) && !button.Recording && button.Binding == Keys.F1 && editor.Visible,
                "Escape cancels capture and preserves previous binding");
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

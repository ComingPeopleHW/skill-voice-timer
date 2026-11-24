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
    private static void CheckSelectionTimers()
    {
        var shield = Bound("护盾", Keys.Home);
        var trap = Bound("陷阱", Keys.End);
        var modifier = Bound("疾跑", Keys.ControlKey);
        var skills = new[] { shield, trap, modifier };
        var timers = new SkillTimers();
        timers.Configure(skills);
        Press(timers, shield.Key, 0);
        Press(timers, trap.Key, 100);
        shield.Enabled = false;
        timers.Configure(skills);
        Press(timers, shield.Key, 1000);
        Check(timers.TakeDue(55000) == null && timers.TakeDue(55100) == trap && timers.TakeDue(56000) == null,
            "unchecked skill loses its timer and ignores presses while another timer keeps its deadline");

        shield.Enabled = true;
        timers.Configure(skills);
        Check(timers.TakeDue(100000) == null, "checking again never restores a stale countdown");
        Press(timers, shield.Key, 100000);
        shield.Enabled = false;
        timers.Configure(skills);
        Check(timers.TakeDue(155001) == null, "cancelled reminder never enters the speech queue");
        shield.Enabled = true;
        timers.Configure(skills);
        Press(timers, shield.Key, 160000);
        Press(timers, trap.Key, 160000);
        Check(timers.TakeDue(215000) == shield, "first simultaneous skill is dispatched");
        trap.Enabled = false;
        timers.Configure(skills);
        Check(timers.TakeDue(215001) == null, "unchecking an overdue skill removes the waiting announcement");
        timers.KeyEvent(trap.Key, false, true, 220000);
        trap.Enabled = true;
        timers.Configure(skills);
        timers.KeyEvent(trap.Key, false, true, 220010);
        Check(timers.TakeDue(300000) == null, "enabling a held primary ignores its key repeat");
        timers.KeyEvent(trap.Key, true, true, 300010);
        Press(timers, trap.Key, 300020);
        Check(timers.TakeDue(355020) == trap, "fresh press after rechecking triggers a new reminder");

        timers.KeyEvent(Keys.LControlKey, false, true, 360000);
        modifier.Enabled = false;
        timers.Configure(skills);
        modifier.Enabled = true;
        timers.Configure(skills);
        timers.KeyEvent(Keys.LControlKey, true, true, 360010);
        Check(timers.TakeDue(420000) == null, "unchecking a held modifier cancels its pending tap");
        foreach (var skill in skills) skill.Enabled = false;
        timers.Configure(skills);
        Check(!timers.HasEnabledBindings && timers.Status(shield.Binding, 420000) == "未勾选",
            "all unchecked leaves no active bindings");
        timers.KeyEvent(Keys.LControlKey, false, true, 425000);
        modifier.Enabled = true;
        timers.Configure(skills);
        timers.KeyEvent(Keys.LControlKey, true, true, 425010);
        Check(timers.TakeDue(490000) == null, "checking during an unmonitored modifier hold ignores the release");
    }

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);

    private static void CaptureMainPreview(MainWindow window)
    {
        var path = Environment.GetEnvironmentVariable("SKILL_TIMER_MAIN_PREVIEW");
        if (string.IsNullOrEmpty(path)) return;
        window.Refresh();
        using (var bitmap = new Bitmap(window.Width, window.Height))
        {
            bool printed;
            using (var g = Graphics.FromImage(bitmap))
            {
                var dc = g.GetHdc();
                try { printed = PrintWindow(window.Handle, dc, 2); }
                finally { g.ReleaseHdc(dc); }
            }
            if (!printed) window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, window.Size));
            bitmap.Save(path, ImageFormat.Png);
        }
    }

    private static void CheckSelectionUi(MainWindow window, SkillStore store, List<Skill> skills)
    {
        var layout = (TableLayoutPanel)window.Controls[0];
        var grid = layout.Controls.OfType<DataGridView>().Single();
        var start = layout.Controls.OfType<FlowLayoutPanel>().SelectMany(p => p.Controls.OfType<Button>())
            .Single(button => button.Text == "开始监听");
        Check(grid.Rows.Cast<DataGridViewRow>().All(row => Equals(row.Cells["enabled"].Value, true)),
            "old profiles start with all rows checked");
        grid.CurrentCell = grid.Rows[1].Cells["enabled"];
        grid.Focus();
        SendMessage(grid.Handle, 0x0100, (IntPtr)(int)Keys.Space, IntPtr.Zero);
        SendMessage(grid.Handle, 0x0101, (IntPtr)(int)Keys.Space, IntPtr.Zero);
        Application.DoEvents();
        Check(!skills[1].Enabled && Equals(grid.Rows[1].Cells["计时状态"].Value, "未勾选") &&
            !store.Load().Current.Skills[1].Enabled && start.Enabled, "checkbox commits immediately, updates row status and saves");
        grid.CurrentCell = grid.Rows[0].Cells[1];
        start.Focus();
        Application.DoEvents();
        CaptureMainPreview(window);

        using (var editor = new SkillEditor(skills, skills[1]))
        {
            editor.Show(window);
            ((Button)editor.AcceptButton).PerformClick();
            Check(!editor.Result.Enabled, "editing an unchecked skill preserves its opt-out");
            editor.Close();
        }
        grid.Rows[0].Cells["enabled"].Value = false;
        Check(!start.Enabled && store.Load().Current.Skills.All(skill => !skill.Enabled), "cannot start with zero checked skills");
        using (var reopened = new MainWindow(store, store.Load()))
        {
            reopened.Show();
            var restoredGrid = ((TableLayoutPanel)reopened.Controls[0]).Controls.OfType<DataGridView>().Single();
            Check(restoredGrid.Rows.Cast<DataGridViewRow>().All(row => Equals(row.Cells["enabled"].Value, false)),
                "reopening restores an entirely unchecked list");
            reopened.Close();
        }
        grid.Rows[0].Cells["enabled"].Value = true;
        grid.Rows[1].Cells["enabled"].Value = true;
        Check(start.Enabled && store.Load().Current.Skills.All(skill => skill.Enabled), "rechecking restores active bindings");
    }
}

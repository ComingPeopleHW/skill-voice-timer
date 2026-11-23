using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SkillVoiceTimer
{
    internal sealed class MainWindow : Form
    {
        private readonly SkillStore store;
        private List<Skill> skills;
        private readonly SkillTimers timers = new SkillTimers();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Timer pulse = new Timer { Interval = 100 };
        private readonly ListView list = new ListView { Dock = DockStyle.Fill, View = View.Details,
            FullRowSelect = true, MultiSelect = false, HideSelection = false, HeaderStyle = ColumnHeaderStyle.Nonclickable };
        private readonly Button listen = MakeButton("开始监听");
        private readonly Button edit = MakeButton("编辑");
        private readonly Button remove = MakeButton("删除");
        private readonly Button preview = MakeButton("试听");
        private readonly Label status = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
        private readonly NotifyIcon tray = new NotifyIcon { Icon = SystemIcons.Information, Text = "技能语音提醒" };
        private readonly ContextMenuStrip trayMenu = new ContextMenuStrip();
        private readonly ToolStripMenuItem trayListen = new ToolStripMenuItem("开始监听");
        private Voice voice;
        private bool listening;
        private bool modal;
        private bool closing;

        public MainWindow(SkillStore store, List<Skill> skills)
        {
            this.store = store;
            this.skills = skills;
            timers.Configure(skills);
            Text = "技能语音提醒";
            Icon = SystemIcons.Information;
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(740, 425);
            MinimumSize = new Size(670, 365);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), RowCount = 5, ColumnCount = 1 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            Controls.Add(layout);
            var heading = new Label { Text = "技能语音提醒", Dock = DockStyle.Fill,
                Font = new Font(Font.FontFamily, 16, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };
            layout.Controls.Add(heading, 0, 0);
            layout.Controls.Add(new Label { Text = "设置技能后点击“开始监听”。最小化到托盘后，游戏内按键仍可计时。",
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
            var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            var add = MakeButton("添加技能");
            bar.Controls.AddRange(new Control[] { listen, add, edit, remove, preview });
            layout.Controls.Add(bar, 0, 2);
            string[] columns = { "技能名", "技能键", "持续时间", "提前提醒", "计时状态" };
            int[] widths = { 180, 95, 90, 90, 145 };
            for (int i = 0; i < columns.Length; i++) list.Columns.Add(columns[i], widths[i]);
            list.AccessibleName = "技能列表";
            layout.Controls.Add(list, 0, 3);
            layout.Controls.Add(status, 0, 4);

            listen.Click += (s, e) => ToggleListening();
            add.Click += (s, e) => EditSkill(null);
            edit.Click += (s, e) => { if (Selected != null) EditSkill(Selected); };
            remove.Click += (s, e) => RemoveSkill();
            preview.Click += (s, e) =>
            {
                var selected = Selected;
                if (selected == null || !OpenVoice() || voice.Busy) return;
                Speak(selected.Name);
            };
            list.DoubleClick += (s, e) => { if (Selected != null) EditSkill(Selected); };
            list.SelectedIndexChanged += (s, e) => UpdateActions();
            trayMenu.Items.Add("打开主窗口", null, (s, e) => RestoreWindow());
            trayMenu.Items.Add(trayListen);
            trayMenu.Items.Add("退出", null, (s, e) => Close());
            trayListen.Click += (s, e) => ToggleListening();
            tray.ContextMenuStrip = trayMenu;
            tray.DoubleClick += (s, e) => RestoreWindow();
            Resize += (s, e) =>
            {
                if (WindowState == FormWindowState.Minimized) { tray.Visible = true; Hide(); }
            };
            pulse.Tick += (s, e) => TickTimers();
            Populate(skills.FirstOrDefault());
            status.Text = "监听已暂停";
            pulse.Start();
        }

        private static Button MakeButton(string text)
        {
            return new Button { Text = text, AutoSize = true, MinimumSize = new Size(64, 30),
                Margin = new Padding(0, 4, 8, 4), UseVisualStyleBackColor = true };
        }

        private Skill Selected { get { return list.SelectedItems.Count == 0 ? null : (Skill)list.SelectedItems[0].Tag; } }

        private void Populate(Skill selected)
        {
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var skill in skills)
            {
                var item = new ListViewItem(new[] { skill.Name, KeyChoices.Label(skill.Key),
                    skill.Duration + " 秒", skill.Lead + " 秒", timers.Status(skill.Key, clock.ElapsedMilliseconds) });
                item.Tag = skill;
                list.Items.Add(item);
                item.Selected = skill == selected;
            }
            list.EndUpdate();
            UpdateActions();
        }

        private void UpdateActions()
        {
            edit.Enabled = remove.Enabled = Selected != null;
            preview.Enabled = Selected != null && (voice == null || !voice.Busy);
            listen.Enabled = trayListen.Enabled = listening || skills.Count > 0;
        }

        private void RestoreWindow()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            tray.Visible = false;
        }

        private void EditSkill(Skill original)
        {
            modal = true;
            try
            {
                using (var editor = new SkillEditor(skills, original))
                {
                    if (editor.ShowDialog(this) != DialogResult.OK) return;
                    var updated = new List<Skill>(skills);
                    if (original == null) updated.Add(editor.Result);
                    else updated[updated.IndexOf(original)] = editor.Result;
                    Apply(updated, editor.Result);
                }
            }
            finally { modal = false; }
        }

        private void RemoveSkill()
        {
            var selected = Selected;
            if (selected == null) return;
            modal = true;
            try
            {
                if (MessageBox.Show(this, "删除技能“" + selected.Name + "”？", Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                var updated = skills.Where(s => s != selected).ToList();
                Apply(updated, updated.FirstOrDefault());
            }
            finally { modal = false; }
        }

        private void Apply(List<Skill> updated, Skill selected)
        {
            try { store.Save(updated); }
            catch (Exception ex) { Alert("技能设置保存失败。", ex); return; }
            skills = updated;
            timers.Configure(skills);
            if (skills.Count == 0 && listening) ToggleListening();
            Populate(selected);
        }

        private bool OpenVoice()
        {
            if (voice != null) return true;
            try
            {
                voice = new Voice(action =>
                {
                    if (!closing && IsHandleCreated) BeginInvoke(action);
                });
                voice.Failed += VoiceFailed;
                return true;
            }
            catch (Exception ex) { Alert("无法开启语音播报。", ex); return false; }
        }

        private void VoiceFailed(string message)
        {
            if (listening) ToggleListening();
            if (voice != null) { voice.Dispose(); voice = null; }
            Alert("语音播报失败。", new InvalidOperationException(message));
        }

        private void Speak(string name)
        {
            try { voice.Speak(name); }
            catch (Exception ex) { VoiceFailed(ex.GetBaseException().Message); }
        }

        private void ToggleListening()
        {
            if (!listening && (skills.Count == 0 || !OpenVoice())) return;
            try { KeyboardInput.Register(Handle, !listening); }
            catch (Exception ex) { Alert("键盘监听设置失败。", ex); return; }
            listening = !listening;
            if (!listening)
            {
                timers.Clear();
                if (voice != null) voice.Cancel();
            }
            listen.Text = trayListen.Text = listening ? "暂停监听" : "开始监听";
            tray.Text = listening ? "技能语音提醒 · 正在监听" : "技能语音提醒 · 已暂停";
            status.Text = listening ? "正在监听  ·  " + voice.Name : "监听已暂停";
            UpdateActions();
        }

        private void Alert(string title, Exception ex)
        {
            if (!Visible) RestoreWindow();
            status.Text = title;
            MessageBox.Show(this, title + Environment.NewLine + ex.GetBaseException().Message, Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == KeyboardInput.WmInput && listening)
            {
                Keys key;
                bool released;
                if (KeyboardInput.Read(m.LParam, out key, out released))
                    timers.KeyEvent(key, released, !modal, clock.ElapsedMilliseconds);
            }
            base.WndProc(ref m);
        }

        private void TickTimers()
        {
            long now = clock.ElapsedMilliseconds;
            if (listening && !voice.Busy)
            {
                var due = timers.TakeDue(now);
                if (due != null) Speak(due.Name);
            }
            if (Visible)
            {
                foreach (ListViewItem item in list.Items)
                {
                    string current = timers.Status(((Skill)item.Tag).Key, now);
                    if (item.SubItems[4].Text != current) item.SubItems[4].Text = current;
                }
                UpdateActions();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !closing)
            {
                closing = true;
                pulse.Dispose();
                if (listening && IsHandleCreated)
                {
                    try { KeyboardInput.Register(Handle, false); }
                    catch (System.ComponentModel.Win32Exception) { /* Windows also removes the target on teardown. */ }
                }
                listening = false;
                if (voice != null) voice.Dispose();
                tray.Visible = false;
                tray.Dispose();
                trayMenu.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

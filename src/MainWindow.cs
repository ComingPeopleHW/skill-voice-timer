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
        private readonly DataGridView list = new DataGridView {
            Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false, RowHeadersVisible = false, MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, BackgroundColor = SystemColors.Window,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AccessibleName = "技能列表"
        };
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
            layout.Controls.Add(new Label { Text = "勾选要监听的技能，再点击“开始监听”。最小化到托盘后仍可计时。",
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
            var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            var add = MakeButton("添加技能");
            bar.Controls.AddRange(new Control[] { listen, add, edit, remove, preview });
            layout.Controls.Add(bar, 0, 2);
            list.Columns.Add(new DataGridViewCheckBoxColumn {
                Name = "enabled", HeaderText = "监听", Width = 55, AutoSizeMode = DataGridViewAutoSizeColumnMode.None
            });
            string[] columns = { "技能名", "技能键", "持续时间", "提前提醒", "计时状态" };
            int[] weights = { 130, 165, 85, 85, 130 };
            for (int i = 0; i < columns.Length; i++)
                list.Columns.Add(new DataGridViewTextBoxColumn { Name = columns[i], HeaderText = columns[i],
                    FillWeight = weights[i], ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
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
            list.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex >= 0 && e.ColumnIndex > 0) EditSkill((Skill)list.Rows[e.RowIndex].Tag);
            };
            list.SelectionChanged += (s, e) => UpdateActions();
            list.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (list.CurrentCell is DataGridViewCheckBoxCell && list.IsCurrentCellDirty)
                    list.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            list.CellValueChanged += OnSkillChecked;
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
            pulse.Start();
        }

        private static Button MakeButton(string text)
        {
            return new Button { Text = text, AutoSize = true, MinimumSize = new Size(64, 30),
                Margin = new Padding(0, 4, 8, 4), UseVisualStyleBackColor = true };
        }

        private Skill Selected { get { return list.CurrentRow?.Tag as Skill; } }

        private void Populate(Skill selected)
        {
            list.Rows.Clear();
            foreach (var skill in skills)
            {
                int index = list.Rows.Add(skill.Enabled, skill.Name, KeyChoices.Label(skill.Binding),
                    skill.Duration + " 秒", skill.Lead + " 秒", timers.Status(skill.Binding, clock.ElapsedMilliseconds));
                var row = list.Rows[index];
                row.Tag = skill;
                if (skill == selected) list.CurrentCell = row.Cells[1];
            }
            RefreshListeningStatus();
            UpdateActions();
        }

        private void OnSkillChecked(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 0) return;
            var row = list.Rows[e.RowIndex];
            var skill = row.Tag as Skill;
            bool enabled = Equals(row.Cells[0].Value, true);
            if (skill == null || skill.Enabled == enabled) return;

            skill.Enabled = enabled;
            try { store.Save(skills); }
            catch (Exception ex)
            {
                skill.Enabled = !enabled;
                row.Cells[0].Value = skill.Enabled;
                Alert("监听选项保存失败。", ex);
                return;
            }
            timers.Configure(skills);
            row.Cells["计时状态"].Value = timers.Status(skill.Binding, clock.ElapsedMilliseconds);
            RefreshListeningStatus();
            UpdateActions();
        }

        private void RefreshListeningStatus()
        {
            if (!timers.HasEnabledBindings)
                status.Text = listening ? "监听已开启，等待勾选技能" : "请先勾选要监听的技能";
            else
                status.Text = listening ? "正在监听  ·  " + voice.Name : "监听已暂停";
        }

        private void UpdateActions()
        {
            edit.Enabled = remove.Enabled = Selected != null;
            preview.Enabled = Selected != null && (voice == null || !voice.Busy);
            listen.Enabled = trayListen.Enabled = listening || timers.HasEnabledBindings;
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
            if (!listening && (!timers.HasEnabledBindings || !OpenVoice())) return;
            try { KeyboardInput.Register(Handle, !listening); }
            catch (Exception ex) { Alert("键盘监听设置失败。", ex); return; }
            listening = !listening;
            if (listening)
            {
                foreach (var key in KeyChoices.SidedModifiers)
                    if (KeyboardInput.IsDown(key)) timers.KeyEvent(key, false, false, clock.ElapsedMilliseconds);
            }
            else
            {
                timers.Clear();
                if (voice != null) voice.Cancel();
            }
            listen.Text = trayListen.Text = listening ? "暂停监听" : "开始监听";
            tray.Text = listening ? "技能语音提醒 · 正在监听" : "技能语音提醒 · 已暂停";
            RefreshListeningStatus();
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
                {
                    bool togglingCheckbox = key == Keys.Space && list.ContainsFocus && list.CurrentCell is DataGridViewCheckBoxCell;
                    timers.KeyEvent(key, released, !modal && !togglingCheckbox, clock.ElapsedMilliseconds);
                }
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
                foreach (DataGridViewRow row in list.Rows)
                {
                    string current = timers.Status(((Skill)row.Tag).Binding, now);
                    var cell = row.Cells["计时状态"];
                    if (!Equals(cell.Value, current)) cell.Value = current;
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

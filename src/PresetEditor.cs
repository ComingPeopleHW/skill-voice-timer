using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SkillVoiceTimer
{
    internal sealed class PresetEditor : Form
    {
        private readonly TextBox nameBox = new TextBox { MaxLength = 40, Dock = DockStyle.Fill, AccessibleName = "预设名称" };
        private readonly CheckBox copy = new CheckBox { AutoSize = true, Checked = true };
        public string PresetName { get { return nameBox.Text.Trim(); } }
        public bool CopySkills { get { return copy.Checked; } }

        public PresetEditor(TimerSettings settings, bool rename)
        {
            Text = rename ? "重命名预设" : "新建预设";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = MaximizeBox = ShowInTaskbar = false;
            ClientSize = new Size(385, rename ? 145 : 174);
            var layout = new TableLayoutPanel { Padding = new Padding(14), ColumnCount = 1, Dock = DockStyle.Fill };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(layout);
            layout.Controls.Add(new Label { Text = "预设名称", AutoSize = true });
            layout.Controls.Add(nameBox);
            if (!rename)
            {
                copy.Text = "复制当前预设的技能和勾选状态";
                layout.Controls.Add(copy);
            }
            var feedback = new Label { AutoSize = true };
            layout.Controls.Add(feedback);
            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel };
            var save = new Button { Text = rename ? "保存" : "创建", DialogResult = DialogResult.OK };
            buttons.Controls.AddRange(new Control[] { cancel, save });
            layout.Controls.Add(buttons);
            CancelButton = cancel;
            AcceptButton = save;
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var preset in settings.Presets)
                if (!rename || preset != settings.Current) existing.Add(preset.Name);
            nameBox.TextChanged += (s, e) =>
            {
                bool duplicate = existing.Contains(PresetName);
                feedback.Text = duplicate ? "这个名称已经被使用。" : "";
                save.Enabled = PresetName.Length > 0 && !duplicate;
            };
            save.Enabled = false;
            nameBox.Text = rename ? settings.Current.Name : "";
            Shown += (s, e) => { nameBox.Focus(); nameBox.SelectAll(); };
        }
    }
}

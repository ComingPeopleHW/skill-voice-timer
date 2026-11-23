using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SkillVoiceTimer
{
    internal sealed class SkillEditor : Form
    {
        private readonly TextBox nameBox = new TextBox { MaxLength = 40 };
        private readonly ComboBox keyBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly NumericUpDown duration = new NumericUpDown { Minimum = 1, Maximum = 86400 };
        private readonly NumericUpDown lead = new NumericUpDown { Minimum = 0, Maximum = 86399 };
        private readonly Label hint = new Label { AutoSize = true };
        private readonly Button save = new Button { Text = "保存", AutoSize = true };
        private readonly HashSet<Keys> used;
        public Skill Result { get; private set; }

        public SkillEditor(List<Skill> skills, Skill original)
        {
            used = new HashSet<Keys>(skills.Where(s => s != original).Select(s => s.Key));
            Text = original == null ? "添加技能" : "编辑技能";
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(408, 300);
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2, RowCount = 6 };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 4; i++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(grid);
            AddField(grid, 0, "技能名", nameBox);
            AddField(grid, 1, "技能键", keyBox);
            AddField(grid, 2, "持续时间（秒）", duration);
            AddField(grid, 3, "提前提醒（秒）", lead);
            grid.Controls.Add(hint, 0, 4);
            grid.SetColumnSpan(hint, 2);
            hint.Anchor = AnchorStyles.Left;
            hint.MaximumSize = new Size(360, 0);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
            var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(save);
            grid.Controls.Add(buttons, 0, 5);
            grid.SetColumnSpan(buttons, 2);
            CancelButton = cancel;
            AcceptButton = save;

            keyBox.Format += (s, e) => { e.Value = KeyChoices.Label((Keys)e.ListItem); };
            keyBox.FormattingEnabled = true;
            keyBox.Items.AddRange(KeyChoices.All.Cast<object>().ToArray());
            var firstFree = KeyChoices.All.FirstOrDefault(k => !used.Contains(k));
            keyBox.SelectedItem = original == null ? firstFree : original.Key;
            nameBox.Text = original == null ? "" : original.Name;
            duration.Value = original == null ? 60 : original.Duration;
            lead.Value = original == null ? 5 : original.Lead;
            nameBox.TextChanged += (s, e) => RefreshHint();
            keyBox.SelectedIndexChanged += (s, e) => RefreshHint();
            duration.ValueChanged += (s, e) => RefreshHint();
            lead.ValueChanged += (s, e) => RefreshHint();
            save.Click += (s, e) =>
            {
                Result = new Skill { Name = nameBox.Text.Trim(), Key = (Keys)keyBox.SelectedItem,
                    Duration = (int)duration.Value, Lead = (int)lead.Value };
                DialogResult = DialogResult.OK;
            };
            RefreshHint();
        }

        private static void AddField(TableLayoutPanel panel, int row, string caption, Control input)
        {
            panel.Controls.Add(new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            input.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            input.AccessibleName = caption;
            panel.Controls.Add(input, 1, row);
        }

        private void RefreshHint()
        {
            save.Enabled = false;
            if (string.IsNullOrWhiteSpace(nameBox.Text)) hint.Text = "输入希望听到的技能名。";
            else if (keyBox.SelectedItem == null || used.Contains((Keys)keyBox.SelectedItem)) hint.Text = "请选择一个尚未绑定的按键。";
            else if (lead.Value >= duration.Value) hint.Text = "提前量必须小于持续时间。";
            else
            {
                hint.Text = string.Format("按键后第 {0} 秒播报“{1}”。", duration.Value - lead.Value, nameBox.Text.Trim());
                save.Enabled = true;
            }
        }
    }
}

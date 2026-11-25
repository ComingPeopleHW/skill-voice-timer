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
        private readonly ShortcutButton keyButton;
        private readonly NumericUpDown duration = new NumericUpDown { Minimum = 1, Maximum = 86400 };
        private readonly NumericUpDown lead = new NumericUpDown { Minimum = 0, Maximum = 86399 };
        private readonly ComboBox reserveFor = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly NumericUpDown buffer = new NumericUpDown { Minimum = 0, Maximum = 86400 };
        private readonly Label hint = new Label { AutoSize = true };
        private readonly Button save = new Button { Text = "保存", AutoSize = true };
        public Skill Result { get; private set; }

        public SkillEditor(List<Skill> skills, Skill original)
        {
            var others = skills.Where(s => s != original).ToDictionary(s => s.Binding, s => s.Name);
            keyButton = new ShortcutButton(original == null ? Keys.None : original.Binding, others);
            Deactivate += (s, e) => keyButton.EndCapture();
            Text = original == null ? "添加技能" : "编辑技能";
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(452, 386);
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2, RowCount = 8 };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 6; i++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(grid);
            AddField(grid, 0, "技能名", nameBox);
            AddField(grid, 1, "技能快捷键", keyButton);
            AddField(grid, 2, "持续时间（秒）", duration);
            AddField(grid, 3, "提前提醒（秒）", lead);
            AddField(grid, 4, "为哪个技能留用", reserveFor);
            AddField(grid, 5, "操作余量（秒）", buffer);
            grid.Controls.Add(hint, 0, 6);
            grid.SetColumnSpan(hint, 2);
            hint.Anchor = AnchorStyles.Left;
            hint.MaximumSize = new Size(360, 0);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
            var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(save);
            grid.Controls.Add(buttons, 0, 7);
            grid.SetColumnSpan(buttons, 2);
            CancelButton = cancel;
            AcceptButton = save;

            nameBox.Text = original == null ? "" : original.Name;
            duration.Value = original == null ? 60 : original.Duration;
            lead.Value = original == null ? 5 : original.Lead;
            var targets = skills.Where(s => s != original).ToList();
            reserveFor.Items.Add("不关联");
            foreach (var target in targets)
                reserveFor.Items.Add(target.Name + "（" + KeyChoices.Label(target.Binding) + "）");
            reserveFor.SelectedIndex = targets.FindIndex(s => s.Id == original?.ReserveForId) + 1;
            buffer.Value = original?.BufferSeconds ?? 3;
            reserveFor.SelectedIndexChanged += (s, e) => RefreshHint();
            buffer.ValueChanged += (s, e) => RefreshHint();
            nameBox.TextChanged += (s, e) => RefreshHint();
            keyButton.CaptureChanged += (s, e) => RefreshHint();
            duration.ValueChanged += (s, e) => RefreshHint();
            lead.ValueChanged += (s, e) => RefreshHint();
            save.Click += (s, e) =>
            {
                Result = new Skill { Name = nameBox.Text.Trim(), Enabled = original == null || original.Enabled,
                    Id = original?.Id ?? Guid.NewGuid(),
                    ReserveForId = reserveFor.SelectedIndex > 0 ? targets[reserveFor.SelectedIndex - 1].Id : (Guid?)null,
                    BufferSeconds = (int)buffer.Value,
                    Key = keyButton.Binding & Keys.KeyCode,
                    Modifiers = keyButton.Binding & KeyChoices.StandardModifiers,
                    WithWindowsKey = (keyButton.Binding & KeyChoices.Win) != 0,
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
            buffer.Enabled = reserveFor.SelectedIndex > 0;
            save.Enabled = false;
            if (keyButton.Recording) hint.Text = keyButton.Hint;
            else if (string.IsNullOrWhiteSpace(nameBox.Text)) hint.Text = "输入希望听到的技能名。";
            else if (keyButton.Binding == Keys.None) hint.Text = "点击“录入快捷键”，按下游戏中使用的按键。";
            else if (lead.Value >= duration.Value) hint.Text = "提前量必须小于持续时间。";
            else
            {
                hint.Text = buffer.Enabled
                    ? string.Format("按键后第 {0} 秒提醒。关联技能剩余 CD ≤ {1} 秒时，跳过本次播报。", duration.Value - lead.Value, duration.Value + buffer.Value)
                    : string.Format("按键后第 {0} 秒播报“{1}”。", duration.Value - lead.Value, nameBox.Text.Trim());
                save.Enabled = true;
            }
        }
    }
}

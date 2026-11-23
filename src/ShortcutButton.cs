using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace SkillVoiceTimer
{
    internal sealed class ShortcutButton : Button
    {
        private readonly Dictionary<Keys, string> otherBindings;
        private Keys consumedKey;

        public Keys Binding { get; private set; }
        public bool Recording { get; private set; }
        public string Hint { get; private set; }
        public event EventHandler CaptureChanged;

        public ShortcutButton(Keys current, Dictionary<Keys, string> otherBindings)
        {
            this.otherBindings = otherBindings;
            Binding = current;
            ImeMode = ImeMode.Disable;
            AccessibleDescription = "点击后按下技能快捷键，Esc 取消录入。";
            UpdateCaption();
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Focus();
            Recording = !Recording;
            Hint = "按下技能键，可同时按住 Ctrl、Alt 或 Shift。Esc 取消录入。";
            UpdateCaption();
        }

        public void EndCapture()
        {
            if (!Recording) return;
            Recording = false;
            UpdateCaption();
        }

        protected override void OnLeave(EventArgs e)
        {
            EndCapture();
            base.OnLeave(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            var primary = keyData & Keys.KeyCode;
            bool repeated = (msg.LParam.ToInt64() & (1L << 30)) != 0;
            if (repeated && (Recording || primary == consumedKey)) return true;
            if (!Recording) return base.ProcessCmdKey(ref msg, keyData);

            consumedKey = primary;
            if (primary == Keys.Escape) { EndCapture(); return true; }
            if (KeyChoices.ModifierFlag(primary) != Keys.None) return true;
            if (!KeyChoices.All.Contains(primary) || KeyboardInput.IsDown(Keys.LWin) || KeyboardInput.IsDown(Keys.RWin))
            {
                Hint = "请换一个技能键，可搭配 Ctrl、Alt 或 Shift。Esc 取消。";
                UpdateCaption();
                return true;
            }

            string owner;
            if (otherBindings.TryGetValue(keyData, out owner))
            {
                Hint = KeyChoices.Label(keyData) + " 已绑定“" + owner + "”。请按其他快捷键，或按 Esc 取消。";
                UpdateCaption();
                return true;
            }

            Binding = keyData;
            EndCapture();
            return true;
        }

        protected override void WndProc(ref Message m)
        {
            const int KeyUp = 0x0101, SystemKeyUp = 0x0105;
            if ((m.Msg == KeyUp || m.Msg == SystemKeyUp) && consumedKey == (Keys)m.WParam.ToInt32())
            {
                consumedKey = Keys.None;
                return;
            }
            base.WndProc(ref m);
        }

        private void UpdateCaption()
        {
            if (Recording) Text = "请按下快捷键…";
            else Text = Binding == Keys.None ? "点击录入快捷键" : KeyChoices.Label(Binding) + "  ·  点击更改";
            CaptureChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

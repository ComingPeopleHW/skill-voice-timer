using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;

namespace SkillVoiceTimer
{
    internal sealed class ShortcutButton : Button
    {
        private readonly Dictionary<Keys, string> otherBindings;
        private readonly Stopwatch escapeHold = new Stopwatch();
        private const int BindEscapeMilliseconds = 600;
        private Keys consumedKey;
        private Keys pendingModifiers;

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
            escapeHold.Reset();
            Recording = !Recording;
            pendingModifiers = Keys.None;
            Hint = "按下技能键或组合键。轻按 Esc 取消，长按 Esc 再松开可绑定。";
            UpdateCaption();
        }

        public void EndCapture()
        {
            if (!Recording) return;
            escapeHold.Reset();
            Recording = false;
            pendingModifiers = Keys.None;
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
            var modifiers = (keyData & Keys.Modifiers) | KeyboardInput.WindowModifier();
            var flag = KeyChoices.ModifierFlag(primary);
            if (flag != Keys.None)
            {
                pendingModifiers = modifiers | flag;
                return true;
            }
            var binding = primary | modifiers;
            if (binding == Keys.Escape)
            {
                pendingModifiers = Keys.None;
                escapeHold.Restart();
                Hint = "轻按松开会取消录入。按住至少 0.6 秒再松开，即可绑定 Esc。";
                UpdateCaption();
            }
            else Record(binding);
            return true;
        }

        private void Record(Keys binding)
        {
            escapeHold.Reset();
            pendingModifiers = Keys.None;
            if (!KeyChoices.CanBind(binding))
            {
                Hint = "请按一个键盘上的按键。Esc 取消。";
                UpdateCaption();
                return;
            }

            string owner;
            if (otherBindings.TryGetValue(binding, out owner))
            {
                Hint = KeyChoices.Label(binding) + " 已绑定“" + owner + "”。请按其他快捷键，或按 Esc 取消。";
                UpdateCaption();
                return;
            }

            Binding = binding;
            EndCapture();
        }

        protected override void WndProc(ref Message m)
        {
            const int KeyUp = 0x0101, SystemKeyUp = 0x0105;
            if (m.Msg == KeyUp || m.Msg == SystemKeyUp)
            {
                var key = (Keys)m.WParam.ToInt32();
                if (Recording && key == Keys.Escape && escapeHold.IsRunning)
                {
                    if (escapeHold.ElapsedMilliseconds >= BindEscapeMilliseconds) Record(Keys.Escape);
                    else EndCapture();
                    consumedKey = Keys.None;
                    return;
                }
                if (Recording && (pendingModifiers & KeyChoices.ModifierFlag(key)) != 0)
                {
                    Record(KeyChoices.ModifierShortcut(pendingModifiers));
                    consumedKey = Keys.None;
                    return;
                }
                // Some keyboards deliver Print Screen only on release.
                if (Recording && key == Keys.PrintScreen && consumedKey != key)
                {
                    Record(key | ModifierKeys | KeyboardInput.WindowModifier());
                    return;
                }
                if (consumedKey == key) { consumedKey = Keys.None; return; }
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

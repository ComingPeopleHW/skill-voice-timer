using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using System.Threading;
using System.Windows.Forms;
using SkillVoiceTimer;

internal static partial class Checks
{
    private static void SubmitPreset(Button action, Action<PresetEditor> fill)
    {
        Exception failure = null;
        using (var click = new System.Windows.Forms.Timer { Interval = 30 })
        {
            click.Tick += (s, e) =>
            {
                var dialog = Application.OpenForms.OfType<PresetEditor>().FirstOrDefault();
                if (dialog == null || !dialog.Visible) return;
                click.Stop();
                try { fill(dialog); }
                catch (Exception ex) { failure = ex; }
                finally { if (dialog.DialogResult == DialogResult.None) dialog.DialogResult = DialogResult.Cancel; }
            };
            click.Start();
            action.PerformClick();
        }
        if (failure != null) throw failure;
    }

    private static void CheckPresets(MainWindow window, SkillStore store)
    {
        var layout = (TableLayoutPanel)window.Controls[0];
        var controls = layout.Controls.OfType<FlowLayoutPanel>().SelectMany(p => p.Controls.Cast<Control>()).ToArray();
        var selector = controls.OfType<ComboBox>().Single();
        var volume = controls.OfType<NumericUpDown>().Single();
        var add = controls.OfType<Button>().Single(button => button.Text == "新建预设");
        var rename = controls.OfType<Button>().Single(button => button.Text == "重命名");
        var delete = controls.OfType<Button>().Single(button => button.Text == "删除预设");
        var grid = layout.Controls.OfType<DataGridView>().Single();
        Check(selector.Items.Count == 1 && !delete.Enabled && volume.Value == 100, "single default preset at original speech volume");
        volume.Value = 35;
        grid.Rows[1].Cells["enabled"].Value = false;
        SubmitPreset(add, dialog =>
        {
            var fields = (TableLayoutPanel)dialog.Controls[0];
            var name = fields.Controls.OfType<TextBox>().Single();
            var save = (Button)dialog.AcceptButton;
            name.Text = "默认";
            Check(!save.Enabled, "duplicate preset name refused");
            name.Text = "Boss";
            save.PerformClick();
        });
        var copied = store.Load();
        Check(copied.Current.Name == "Boss" && copied.Presets.Count == 2 && !copied.Current.Skills[1].Enabled
            && copied.Current.Skills[0].Binding == copied.Presets[0].Skills[0].Binding,
            "new preset copies keys, selection and becomes active");
        grid.Rows[1].Cells["enabled"].Value = true;
        selector.SelectedIndex = 0;
        Check(Equals(grid.Rows[1].Cells["enabled"].Value, false) && volume.Value == 35,
            "changing copied skills leaves the original intact and volume is shared");
        selector.SelectedIndex = 1;
        SubmitPreset(rename, dialog =>
        {
            var name = ((TableLayoutPanel)dialog.Controls[0]).Controls.OfType<TextBox>().Single();
            name.Text = "周常 Boss";
            ((Button)dialog.AcceptButton).PerformClick();
        });
        Check(store.Load().Current.Name == "周常 Boss" && grid.Rows.Count == 2, "rename retains skills and active selection");
        SubmitPreset(add, dialog =>
        {
            var fields = (TableLayoutPanel)dialog.Controls[0];
            fields.Controls.OfType<TextBox>().Single().Text = "小号";
            fields.Controls.OfType<CheckBox>().Single().Checked = false;
            ((Button)dialog.AcceptButton).PerformClick();
        });
        Check(grid.Rows.Count == 0 && store.Load().Current.Skills.Count == 0 && delete.Enabled,
            "new empty preset can coexist with populated presets");
        selector.SelectedIndex = 1;
        volume.Value = 0;
        using (var reopened = new MainWindow(store, store.Load()))
        {
            reopened.Show();
            var rows = ((TableLayoutPanel)reopened.Controls[0]).Controls.OfType<FlowLayoutPanel>();
            var restored = rows.SelectMany(row => row.Controls.Cast<Control>()).ToArray();
            Check((string)restored.OfType<ComboBox>().Single().SelectedItem == "周常 Boss" &&
                restored.OfType<NumericUpDown>().Single().Value == 0, "restart restores last preset and mute");
            reopened.Close();
        }
        var saved = store.Load();
        var bad = saved.Draft();
        bad.Volume = 101;
        bool rejected = false;
        try { store.Save(bad); } catch (InvalidDataException) { rejected = true; }
        Check(rejected && store.Load().Volume == 0, "invalid volume leaves previous settings intact");

        var timers = new SkillTimers();
        var oldSkill = Bound("旧技能", Keys.Home);
        var newSkill = Bound("新技能", Keys.Home);
        timers.Configure(new[] { oldSkill });
        timers.KeyEvent(Keys.Home, false, true, 0);
        timers.SwitchPreset(new[] { newSkill });
        timers.KeyEvent(Keys.Home, false, true, 100);
        Check(timers.TakeDue(100000) == null, "switch clears old timers and ignores autorepeat from the old preset");
        timers.KeyEvent(Keys.Home, true, true, 100010);
        Press(timers, Keys.Home, 100020);
        Check(timers.TakeDue(155020) == newSkill, "same key fires only the active preset's skill after release");
    }

    private static double SpeechEnergy(int volume)
    {
        using (var pcm = new MemoryStream())
        {
            var engine = new SpeechSynthesizer();
            engine.SetOutputToAudioStream(pcm, new SpeechAudioFormatInfo(16000, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
            using (var speaker = new Voice(engine, action => action()))
            {
                string error = null;
                speaker.Failed += message => error = message;
                speaker.Speak("Skill reminder", volume);
                var wait = Stopwatch.StartNew();
                while (speaker.Busy && wait.ElapsedMilliseconds < 10000)
                {
                    Application.DoEvents();
                    Thread.Sleep(5);
                }
                if (speaker.Busy || error != null) throw new Exception(error ?? "Speech timed out.");
            }
            byte[] bytes = pcm.ToArray();
            if (bytes.Length < 2) return 0;
            double energy = 0;
            for (int i = 0; i + 1 < bytes.Length; i += 2)
            {
                double sample = BitConverter.ToInt16(bytes, i);
                energy += sample * sample;
            }
            return energy / (bytes.Length / 2);
        }
    }

    private static void CheckVolumeOutput()
    {
        using (var inventory = new SpeechSynthesizer())
            if (!inventory.GetInstalledVoices().Any(v => v.Enabled))
            {
                Console.WriteLine("No installed desktop voice; audio rendering check skipped.");
                return;
            }
        double loud = SpeechEnergy(100);
        double quiet = SpeechEnergy(25);
        double mute = SpeechEnergy(0);
        Check(loud > quiet && quiet > 0 && mute == 0, "speech engine renders quieter PCM at 25 percent and silence at zero");
    }
}

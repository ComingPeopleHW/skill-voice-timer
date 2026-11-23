using System;
using System.Linq;
using System.Speech.Synthesis;

namespace SkillVoiceTimer
{
    internal sealed class Voice : IDisposable
    {
        private readonly SpeechSynthesizer engine = new SpeechSynthesizer();
        private readonly Action<Action> postToWindow;
        private Prompt active;
        private bool disposed;
        public string Name { get; private set; }
        public bool Busy { get { return active != null; } }
        public event Action<string> Failed;

        public Voice(Action<Action> postToWindow)
        {
            this.postToWindow = postToWindow;
            try
            {
                var chinese = engine.GetInstalledVoices().Where(v => v.Enabled &&
                    v.VoiceInfo.Culture.TwoLetterISOLanguageName == "zh")
                    .OrderByDescending(v => v.VoiceInfo.Culture.Name == "zh-CN").FirstOrDefault();
                if (chinese == null)
                    throw new InvalidOperationException("未找到中文语音。请在 Windows 语言设置中安装中文的“文本到语音”，再点“试听”。");
                Name = chinese.VoiceInfo.Name;
                engine.SelectVoice(Name);
                engine.SetOutputToDefaultAudioDevice();
                engine.SpeakCompleted += OnCompleted;
            }
            catch { engine.Dispose(); throw; }
        }

        public void Speak(string name)
        {
            active = engine.SpeakAsync(name);
        }

        private void OnCompleted(object sender, SpeakCompletedEventArgs e)
        {
            postToWindow(() =>
            {
                if (disposed || e.Prompt != active) return;
                active = null;
                if (!e.Cancelled && e.Error != null && Failed != null) Failed(e.Error.Message);
            });
        }

        public void Cancel()
        {
            if (Busy) engine.SpeakAsyncCancelAll();
        }

        public void Dispose()
        {
            disposed = true;
            engine.SpeakCompleted -= OnCompleted;
            engine.Dispose();
        }
    }
}

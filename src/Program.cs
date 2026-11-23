using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace SkillVoiceTimer
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool first;
            using (var instance = new Mutex(true, @"Local\SkillVoiceTimer.Main", out first))
            {
                if (!first)
                {
                    MessageBox.Show("技能语音提醒已在运行，可从任务栏通知区域打开。", "技能语音提醒");
                    return;
                }
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SkillVoiceTimer", "skills.xml");
                var store = new SkillStore(path);
                try
                {
                    var skills = store.Load();
                    using (var window = new MainWindow(store, skills)) Application.Run(window);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("启动失败。" + Environment.NewLine + ex.GetBaseException().Message +
                        Environment.NewLine + "配置文件位于 " + path, "技能语音提醒", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}

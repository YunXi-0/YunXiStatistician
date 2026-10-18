using System.Diagnostics;
using System.Security.Principal;

namespace PcCompanionMonitor;

internal static class AdminPrompt
{
    private const string SuppressFileName = "admin_prompt_suppressed";

    public static bool ShowIfNeeded()
    {
        if (IsAdministrator())
        {
            return true;
        }
        if (IsSuppressed())
        {
            RestartElevated();
            return false;
        }

        using Form dialog = new()
        {
            Text = "云曦PC统计权限提示",
            ClientSize = new Size(420, 190),
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = true,
        };
        Label message = new()
        {
            Text = "为了监听游戏内的键盘和鼠标操作，云曦PC统计需要管理员权限。\r\n不提权时，普通桌面操作仍可统计，但部分游戏可能无法记录。",
            Location = new Point(20, 20),
            Size = new Size(380, 56),
        };
        CheckBox suppress = new()
        {
            Text = "不再提醒",
            Location = new Point(20, 92),
            AutoSize = true,
        };
        Button elevate = new()
        {
            Text = "以管理员身份运行",
            DialogResult = DialogResult.Yes,
            Location = new Point(190, 135),
            Size = new Size(130, 30),
        };
        Button continueButton = new()
        {
            Text = "暂不提权",
            DialogResult = DialogResult.No,
            Location = new Point(330, 135),
            Size = new Size(70, 30),
        };
        dialog.Controls.AddRange([message, suppress, elevate, continueButton]);
        dialog.AcceptButton = elevate;
        dialog.CancelButton = continueButton;

        DialogResult result = dialog.ShowDialog();
        if (result != DialogResult.Yes)
        {
            return true;
        }
        if (suppress.Checked)
        {
            SaveSuppressed();
        }

        RestartElevated();
        return false;
    }

    private static void RestartElevated()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Environment.ProcessPath!,
                UseShellExecute = true,
                Verb = "runas",
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"提权启动失败：{ex.Message}", "云曦PC统计", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static bool IsAdministrator()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string SuppressPath => Path.Combine(new DailyDataStore().DataDirectory, SuppressFileName);

    private static bool IsSuppressed() => File.Exists(SuppressPath);

    private static void SaveSuppressed()
    {
        try
        {
            AtomicFile.WriteAllText(SuppressPath, "1");
        }
        catch
        {
        }
    }
}

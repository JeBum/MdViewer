using System.Diagnostics;
using System.Reflection;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        try
        {
            var destDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "MDviewer");
            Directory.CreateDirectory(destDir);
            var dest = Path.Combine(destDir, "MDviewer.exe");

            var asm = Assembly.GetExecutingAssembly();
            var name = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("MDviewer.exe", StringComparison.OrdinalIgnoreCase));
            if (name == null)
                throw new InvalidOperationException("Payload missing. Run make_setup.bat");
            using (var src = asm.GetManifestResourceStream(name)!)
            using (var fs = File.Create(dest))
                src.CopyTo(fs);

            var lnk = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                "Programs", "MDviewer.lnk");
            var ps = Path.Combine(Path.GetTempPath(), "mdv_lnk.ps1");
            File.WriteAllText(ps,
                "$s=(New-Object -ComObject WScript.Shell).CreateShortcut(@'" + lnk + "');" +
                "$s.TargetPath=@'" + dest + "';$s.WorkingDirectory=@'" + destDir + "';$s.Save()");
            Process.Start(new ProcessStartInfo("powershell", "-NoProfile -ExecutionPolicy Bypass -File \"" + ps + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            })?.WaitForExit(8000);

            Process.Start(new ProcessStartInfo(dest)
            {
                UseShellExecute = true,
                WorkingDirectory = destDir
            });
            MessageBox.Show("Installed:\n" + dest, "MDviewer Setup",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "MDviewer Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

using System;
using System.Diagnostics;

namespace Pixo.Core
{
    /// <summary>
    /// Iniciar com o Windows. Como o Pixo roda como administrador (para ler as temperaturas),
    /// usamos uma tarefa agendada "ao fazer logon" com privilégios elevados:
    /// assim ele abre sozinho, sem pedir permissão toda vez.
    /// </summary>
    public static class Startup
    {
        private const string TaskName = "Pixo";

        public static bool IsEnabled()
        {
            return Run("/Query /TN \"" + TaskName + "\"") == 0;
        }

        public static bool Enable()
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return false;
            // aspas escapadas para o caminho do .exe e o argumento --minimizado
            var tr = "\\\"" + exe + "\\\" --minimizado";
            return Run("/Create /F /TN \"" + TaskName + "\" /SC ONLOGON /RL HIGHEST /TR \"" + tr + "\"") == 0;
        }

        public static bool Disable()
        {
            return Run("/Delete /F /TN \"" + TaskName + "\"") == 0;
        }

        private static int Run(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe", args)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var p = Process.Start(psi);
                p.WaitForExit(10000);
                return p.ExitCode;
            }
            catch { return -1; }
        }
    }
}

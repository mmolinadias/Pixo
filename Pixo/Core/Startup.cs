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

        /// <summary>
        /// Se a tarefa existe mas aponta para outro Pixo.exe (ex.: o antigo em C:\Pixo, antes do instalador),
        /// refaz apontando para este. Precisa rodar como administrador.
        /// </summary>
        public static void FixPath()
        {
            try
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe) || !IsEnabled()) return;
                var psi = new ProcessStartInfo("schtasks.exe", "/Query /TN \"" + TaskName + "\" /XML")
                {
                    CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true,
                };
                using var p = Process.Start(psi);
                string xml = p.StandardOutput.ReadToEnd();
                p.WaitForExit(10000);
                if (xml.Length > 0 && xml.IndexOf(exe, StringComparison.OrdinalIgnoreCase) < 0) Enable();
            }
            catch { }
        }

        public static bool Disable()
        {
            return Run("/Delete /F /TN \"" + TaskName + "\"") == 0;
        }

        /// <summary>
        /// Na desinstalação (o desinstalador roda sem administrador): tenta apagar a tarefa normalmente e,
        /// se não der, pede permissão uma vez para apagar.
        /// </summary>
        public static void DisableForUninstall()
        {
            if (!IsEnabled() || Disable()) return;
            try
            {
                using var p = Process.Start(new ProcessStartInfo("schtasks.exe", "/Delete /F /TN \"" + TaskName + "\"")
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden,
                });
                p?.WaitForExit(20000);
            }
            catch { /* recusou: a tarefa fica, mas só aponta para um programa que não existe mais */ }
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

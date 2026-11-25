using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using Velopack;

namespace Pixo
{
    /// <summary>
    /// Entrada do programa.
    /// 1) Velopack (instalador/atualizador) precisa rodar antes de tudo: na instalação, desinstalação e
    ///    atualização ele chama o Pixo.exe com argumentos próprios e o processo termina ali.
    /// 2) O Pixo precisa de administrador para ler as temperaturas. O manifesto pede só o normal ("asInvoker")
    ///    para o instalador não esbarrar no aviso do Windows; aqui ele se reabre como administrador.
    ///    A tarefa "Iniciar com o Windows" já abre como administrador, sem perguntar.
    /// </summary>
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            VelopackApp.Build()
                .OnBeforeUninstallFastCallback(_ => Core.Startup.DisableForUninstall())
                .Run();

            bool admin = IsAdmin();
            Log($"abriu: admin={admin} args=[{string.Join(" ", args)}] exe={Environment.ProcessPath}");
            if (!admin && !args.Contains("--sem-admin", StringComparer.OrdinalIgnoreCase))
            {
                if (AlreadyRunning()) { Log("já tem um Pixo aberto: sai"); return 0; }   // não pede permissão à toa
                if (RelaunchAsAdmin(args)) { Log("reabriu como administrador"); return 0; }
                // recusou a permissão: abre assim mesmo (sem as temperaturas da CPU/placa)
                Log("segue sem administrador");
            }

            var app = new App();
            app.InitializeComponent();
            return app.Run();
        }

        public static bool IsAdmin()
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }

        private static bool AlreadyRunning()
        {
            try
            {
                if (Mutex.TryOpenExisting("Pixo.SingleInstance", out var m)) { m.Dispose(); return true; }
                return false;
            }
            catch (UnauthorizedAccessException) { return true; } // existe, mas é do Pixo que roda como administrador
            catch { return false; }
        }

        private static bool RelaunchAsAdmin(string[] args)
        {
            try
            {
                var psi = new ProcessStartInfo(Environment.ProcessPath)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    Arguments = string.Join(" ", args.Select(a => a.Contains(' ') ? "\"" + a + "\"" : a)),
                };
                Process.Start(psi);
                return true;
            }
            catch (Win32Exception ex) { Log($"pedido de administrador falhou: {ex.NativeErrorCode} {ex.Message}"); return false; } // 1223 = clicou em "Não"
            catch (Exception ex) { Log("pedido de administrador falhou: " + ex.Message); return false; }
        }

        /// <summary>Diagnóstico da abertura: %AppData%\Pixo\inicio.txt (só as últimas linhas).</summary>
        private static void Log(string line)
        {
            try
            {
                var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pixo", "inicio.txt");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                var lines = System.IO.File.Exists(path) ? System.IO.File.ReadAllLines(path).ToList() : new System.Collections.Generic.List<string>();
                lines.Add(DateTime.Now.ToString("dd/MM HH:mm:ss") + "  " + line);
                System.IO.File.WriteAllLines(path, lines.Skip(Math.Max(0, lines.Count - 40)));
            }
            catch { }
        }
    }
}

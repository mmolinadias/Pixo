using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace Pixo.Core
{
    /// <summary>
    /// Atualização automática (Velopack + Releases do GitHub). Ao abrir (e a cada 6 h) procura versão nova,
    /// baixa em segundo plano e aplica quando o Pixo fecha; o botão da aba Geral aplica na hora.
    /// Só funciona quando o Pixo foi instalado pelo instalador (Setup.exe).
    /// </summary>
    public static class Updater
    {
        public const string RepoUrl = "https://github.com/mmolinadias/Pixo";

        private static UpdateManager _mgr;
        private static UpdateInfo _ready;
        private static Timer _timer;
        private static int _busy;

        public static event Action Changed;

        /// <summary>Frase para a aba Geral.</summary>
        public static string Status { get; private set; } = "";
        public static bool Installed => _mgr?.IsInstalled == true;
        public static bool ReadyToApply => _ready != null;

        public static string Version
        {
            get
            {
                try { if (_mgr?.CurrentVersion != null) return _mgr.CurrentVersion.ToString(); } catch { }
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return v == null ? "?" : $"{v.Major}.{v.Minor}.{v.Build}";
            }
        }

        public static void Start()
        {
            try { _mgr = new UpdateManager(new GithubSource(RepoUrl, null, false)); }
            catch { _mgr = null; }
            if (!Installed)
            {
                Set("Atualização automática só funciona no Pixo instalado pelo Setup.exe.");
                return;
            }
            Set("Versão " + Version);
            // primeira olhada 1 minuto depois de abrir; depois, a cada 6 horas
            _timer = new Timer(_ => _ = Check(), null, TimeSpan.FromMinutes(1), TimeSpan.FromHours(6));
        }

        public static void Stop() => _timer?.Dispose();

        /// <summary>Procura versão nova e, se tiver, baixa. Devolve quando terminar.</summary>
        public static async Task Check()
        {
            if (!Installed || _ready != null) return;
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            try
            {
                Set("Procurando versão nova…");
                var info = await _mgr.CheckForUpdatesAsync();
                if (info == null) { Set($"Você está na versão mais nova ({Version})."); return; }
                var v = info.TargetFullRelease.Version.ToString();
                Set($"Baixando a versão {v}…");
                await _mgr.DownloadUpdatesAsync(info, p => Set($"Baixando a versão {v}… {p}%"));
                _ready = info;
                Set($"Versão {v} pronta. Ela entra quando o Pixo reiniciar.");
            }
            catch (Exception ex) { Set("Não deu para procurar atualização agora (" + ex.Message + ")."); }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }

        /// <summary>Aplica a versão baixada agora e reabre o Pixo.</summary>
        public static void ApplyNow()
        {
            if (_ready == null) return;
            App.Cfg?.Save();
            _mgr.ApplyUpdatesAndRestart(_ready.TargetFullRelease, new[] { "--minimizado" });
        }

        /// <summary>Ao sair do Pixo: se tem versão baixada, ela é instalada depois que o processo fechar.</summary>
        public static void ApplyOnExit()
        {
            try { if (_ready != null) _mgr.WaitExitThenApplyUpdates(_ready.TargetFullRelease, true, false); }
            catch { }
        }

        private static void Set(string s)
        {
            Status = s;
            Changed?.Invoke();
        }
    }
}

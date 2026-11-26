using System;
using System.IO;
using System.Linq;

namespace Pixo.Core
{
    /// <summary>
    /// Tela por app em foco: com um app da lista na frente (ex.: Spotify → Música), mostra a tela dele e segura a
    /// rotação; ao sair do app, volta para a tela de antes. Espera o app ficar 3 s em foco (para não piscar ao
    /// trocar de janela) e não insiste se você trocou de tela na mão (até mudar de app).
    /// </summary>
    public sealed class AppFocus
    {
        public const double SettleSeconds = 3;

        private readonly Func<Config> _cfg;
        private string _app, _before, _override;
        private DateTime _since = DateTime.MinValue;

        /// <summary>Tela que o app em foco está segurando (null = rotação normal).</summary>
        public string Mode { get; private set; }

        public AppFocus(Func<Config> cfg) { _cfg = cfg; }

        /// <summary>
        /// Chamado a cada volta do Engine. Devolve a tela para mostrar agora, "" para só recomeçar o tempo da
        /// rotação (saiu do app e não tinha tela de antes), ou null para não mexer.
        /// </summary>
        /// <param name="exe">caminho do .exe em primeiro plano</param>
        /// <param name="inGame">Modo Gamer ligado (tem prioridade)</param>
        /// <param name="hold">um aviso ou a voz pediu outra tela por um tempo: espera</param>
        /// <param name="current">tela que está aparecendo</param>
        /// <param name="hasContent">se uma tela tem o que mostrar agora</param>
        public string Tick(string exe, DateTime now, bool inGame, bool hold, string current, Func<string, bool> hasContent)
        {
            string app = string.IsNullOrEmpty(exe) ? null : Path.GetFileName(exe).ToLowerInvariant();
            if (app != _app) { _app = app; _since = now; _override = null; }

            var c = _cfg();
            if (inGame || !c.AppScreensEnabled) { Mode = null; _before = null; return null; }
            if ((now - _since).TotalSeconds < SettleSeconds || hold) return null;

            var rule = app == null || app == _override ? null
                : c.AppScreens?.FirstOrDefault(r => string.Equals(r.App?.Trim(), app, StringComparison.OrdinalIgnoreCase));
            string target = rule?.Screen != null && hasContent(rule.Screen) ? rule.Screen : null;

            if (target != null)
            {
                string show = null;
                if (current != target)
                {
                    if (Mode == null) _before = current;
                    show = target;
                }
                Mode = target;
                return show;
            }
            if (Mode != null)
            {
                var back = _before;
                Mode = null;
                _before = null;
                return back != null && back != current && hasContent(back) ? back : "";
            }
            return null;
        }

        /// <summary>Você trocou de tela na mão: o app em foco não força mais a tela dele (até mudar de app).</summary>
        public void Manual()
        {
            _override = _app;
            Mode = null;
            _before = null;
        }
    }
}

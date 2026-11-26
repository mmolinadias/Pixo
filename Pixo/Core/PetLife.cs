using System;
using System.Collections.Generic;
using System.Linq;
using Pixo.Data;
using Pixo.Render;

namespace Pixo.Core
{
    /// <summary>
    /// O "dia a dia" do Fred e do Dengo, fora da tela deles: humor que cai sem atenção e sobe com carinho,
    /// susto com pico de CPU/temperatura, festa quando um jogo termina e lembrete de pausa depois de muito
    /// tempo seguido no PC. Roda na thread do Engine (Tick a cada volta do loop).
    /// </summary>
    public sealed class PetLife
    {
        private readonly Config _cfg;
        private readonly PetScreen _pet;
        private readonly Action _showPets;   // leva a tela para os bichinhos (fora do Modo Gamer)

        private DateTime _lastTick = DateTime.MinValue;
        private readonly Queue<(double cpu, double temp)> _hist = new Queue<(double, double)>();
        private DateTime _nextScare = DateTime.MinValue;
        private string _game;
        private DateTime _gameStart;
        private DateTime _sessionStart = DateTime.Now;
        private DateTime _nextBreak = DateTime.MinValue;
        private double _decay;

        /// <summary>Quanto o humor cai por hora sem carinho (de 100 a 0 em ~12 h).</summary>
        private const double MoodPerHour = 8;

        public PetLife(Config cfg, PetScreen pet, Action showPets)
        {
            _cfg = cfg;
            _pet = pet;
            _showPets = showPets;

            // o tempo com o Pixo fechado conta só um pouco (no máximo 2 h de saudade)
            var away = DateTime.Now - _cfg.PetMoodAt;
            if (away > TimeSpan.Zero)
                _cfg.PetMood -= Math.Min(away.TotalHours, 2) * MoodPerHour;
            _cfg.PetMoodAt = DateTime.Now;
        }

        /// <summary>Carinho (voz, bandeja ou janela): humor sobe e eles mandam coraçõezinhos.</summary>
        public void Carinho()
        {
            _cfg.PetMood += 15;
            _cfg.PetMoodAt = DateTime.Now;
            _pet.React("feliz_coracao");
            _pet.Say(null, _cfg.PetMood >= 95 ? "Assim eles ficam mal acostumados! ♥" : "Ahh, valeu pelo carinho!", 20);
            _showPets();
        }

        public void Tick()
        {
            var now = DateTime.Now;
            if ((now - _lastTick).TotalSeconds < 1) return;
            double dt = _lastTick == DateTime.MinValue ? 0 : (now - _lastTick).TotalHours;
            _lastTick = now;

            // humor: cai devagar enquanto o PC está em uso (parado, eles dormem e não sentem falta)
            // (acumula e desconta de 1 em 1 ponto, para não gravar a configuração a cada segundo)
            if (GameService.IdleSeconds() < 300 && dt > 0 && dt < 0.01)
            {
                _decay += dt * MoodPerHour;
                if (_decay >= 1)
                {
                    _cfg.PetMood -= Math.Floor(_decay);
                    _decay -= Math.Floor(_decay);
                    _cfg.PetMoodAt = now;
                }
            }

            Spikes(now);
            GameEnd(now);
            Break(now);
            ClaudeWait();
        }

        private DateTime _barkedFor = DateTime.MinValue;

        /// <summary>O Claude pediu algo e você não respondeu em 2 minutos: o Fred late (uma vez por espera).</summary>
        private void ClaudeWait()
        {
            var (wait, since) = ClaudeService.LongestWait();
            if (wait == null || wait < TimeSpan.FromMinutes(2) || since == _barkedFor) return;
            _barkedFor = since;
            _pet.React("latido");
            _pet.Say("CLAUDE", $"Au! O Claude tá esperando você há {(int)wait.Value.TotalMinutes} minutos.", 40);
            _showPets();
        }

        /// <summary>Susto quando a CPU dispara de repente ou a temperatura sobe muito rápido.</summary>
        private void Spikes(DateTime now)
        {
            var hw = HardwareService.Current;
            // sem leitura ainda (logo depois de abrir) não conta: 0° → 50° pareceria um "salto"
            if (hw.CpuLoad == null) return;
            double cpu = hw.CpuLoad.Value;
            double temp = new[] { hw.Cpu?.Temp, hw.Gpu?.Temp }.Where(t => t != null && t > 0).Select(t => t.Value).DefaultIfEmpty(0).Max();
            if (temp <= 0 && _hist.Any(h => h.temp > 0)) return; // o sensor falhou neste segundo
            _hist.Enqueue((cpu, temp));
            while (_hist.Count > 12) _hist.Dequeue();
            if (_hist.Count < 12 || now < _nextScare || GameService.Current != null) return;

            var old = _hist.Take(6).ToList();            // de 12 a 6 segundos atrás
            bool cpuJump = cpu >= 90 && old.Average(h => h.cpu) <= 35;
            bool tempJump = temp > 0 && old.All(h => h.temp > 0) && temp - old.Min(h => h.temp) >= 15;
            if (!cpuJump && !tempJump) return;

            _nextScare = now.AddMinutes(3);
            _pet.React("susto");
            _pet.Say(null, cpuJump ? "Eita! A CPU disparou do nada!" : "Opa, esquentou rápido aqui!", 15);
        }

        /// <summary>Festa quando um jogo termina (se durou pelo menos 3 minutos).</summary>
        private void GameEnd(DateTime now)
        {
            var g = GameService.Current;
            if (g != null && _game == null) { _game = g.Name; _gameStart = now; }
            else if (g == null && _game != null)
            {
                if ((now - _gameStart).TotalMinutes >= 3)
                {
                    _pet.React("comemora");
                    _pet.Say("GG", $"Fim de {_game}! Mandou bem.", 30);
                    _showPets();
                }
                _game = null;
            }
        }

        /// <summary>Lembrete de pausa depois de muito tempo seguido no PC (repete a cada 30 min).</summary>
        private void Break(DateTime now)
        {
            if (GameService.IdleSeconds() >= 300) { _sessionStart = now; _nextBreak = DateTime.MinValue; return; }
            double limit = _cfg.PetBreakMinutes;
            if (limit <= 0) return;
            var session = now - _sessionStart;
            if (session.TotalMinutes < limit || now < _nextBreak) return;

            _nextBreak = now.AddMinutes(30);
            string tempo = session.TotalHours >= 1
                ? $"{(int)session.TotalHours}h{(session.Minutes > 0 ? session.Minutes.ToString("00") : "")}"
                : $"{(int)session.TotalMinutes} min";
            _pet.Say("PAUSA", GameService.Current != null
                ? $"Já são {tempo} de jogo. Bebe uma água!"
                : $"Já são {tempo} no PC. Que tal esticar as pernas?", 60);
            if (GameService.Current == null) _showPets();
        }
    }
}

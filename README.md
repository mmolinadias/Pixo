# Pixo

<img src="Pixo/Assets/pixo.ico" width="64" alt="Logo do Pixo">

App para Windows que controla a **Turing Smart Screen 3,5"** deitada na mesa (480×320), feito em .NET/WPF a partir do sistema de design do Pixo.

## O que tem

- **Telas no rodízio:** Temperaturas, Música, Notificações, Downloads, Bichinho, Imagem e Relógio (este começa desligado). As telas sem conteúdo são puladas sozinhas.
- **Modo Gamer automático:** quando um jogo vai para a frente, a tela troca sozinha (FPS, GPU, CPU, VRAM, RAM).
- **O Bit:** o bichinho, com 9 animações (feliz, coração, ocupado, quente, dormindo, jogando, ouvindo música, susto quando chega notificação e comemoração quando um download termina).
- **Voz offline em português:** "temperatura", "música", "próxima", "pausar"... A lista é editável na aba Voz.
- **Bandeja do Windows:** o clique direito abre o menu rápido (anterior/pausar/próxima, brilho, sair) e o clique esquerdo abre as configurações.
- **Janela de configurações** com tema escuro do Windows 11.
- **Iniciar com o Windows** (aba Geral).

## Como compilar e rodar (Visual Studio)

1. Instale o **Visual Studio 2026** (ou 2022 atualizado) com a carga de trabalho **"Desenvolvimento para desktop com .NET"**.
   - O projeto usa o **.NET 10**. Se o seu Visual Studio não tiver o .NET 10, abra `Pixo/Pixo.csproj` e troque `net10.0-windows10.0.19041.0` por `net8.0-windows10.0.19041.0`.
2. Abra o `Pixo.sln`.
3. **Feche o UsbMonitor** (o programa que veio com a tela) e tire ele da inicialização do Windows. Os dois não podem usar a tela ao mesmo tempo.
4. Aperte **F5**. Na primeira vez, o Visual Studio baixa os pacotes sozinho.
   - O Pixo roda como **administrador** (é necessário para ler as temperaturas). O Visual Studio vai pedir para reiniciar como administrador; aceite.
5. Para gerar o programa final: **Compilação › Publicar** ou, no terminal, `dotnet publish -c Release`. O `.exe` fica em `Pixo\bin\Release\...\publish\`.

## Configurar depois de abrir

| O quê | Como |
|---|---|
| **Voz** | Baixe o modelo `vosk-model-small-pt-0.3` em alphacephei.com/vosk/models (o botão "Baixar modelo" na aba Voz abre o site). Descompacte e, na aba Voz, clique em **Escolher pasta**. |
| **FPS no Modo Gamer** | Instale o **MSI Afterburner** (vem com o RivaTuner) e deixe o RivaTuner aberto enquanto joga. |
| **Notificações** | Na primeira vez, o Windows pode pedir permissão. Se a tela avisar que não tem acesso, ative em *Configurações › Privacidade e segurança › Notificações*. |
| **Torrent** | No qBittorrent: *Ferramentas › Opções › Interface Web*. Ative e marque "Ignorar autenticação para clientes no localhost". |
| **Imagens** | Coloque fotos em *Imagens › Pixo* (ou escolha outra pasta na aba Imagens). |
| **Tela de cabeça para baixo** | Aba *Tela física › Orientação › Paisagem invertida*. |

## Se algo não funcionar

- **"Não encontrei a tela":** confira o cabo e se o UsbMonitor está fechado. Se continuar, escolha a porta COM na aba *Tela física*.
- **Temperaturas não aparecem:** o Pixo precisa estar como administrador. As versões novas do LibreHardwareMonitor usam o driver **PawnIO** (pawnio.eu); se a CPU continuar sem temperatura, instale-o.
- **A voz não reconhece:** confira o microfone padrão do Windows e se a pasta do modelo tem a subpasta `am`.
- **Erro de compilação:** copie a mensagem da lista de erros do Visual Studio e me mande.

## Estrutura do código

```
Pixo/
  App.xaml(.cs)          início do app, bandeja, serviços
  Core/Config.cs         configurações (salvas em %AppData%\Pixo\config.json)
  Core/Engine.cs         motor: rodízio, Modo Gamer, comandos, envio dos quadros
  Core/Startup.cs        iniciar com o Windows (tarefa agendada)
  Device/TuringDisplay.cs  driver da tela (serial, RGB565, giro para paisagem, envia só o que muda)
  Data/                  hardware, rede, música, notificações, downloads, jogo/FPS
  Render/Draw.cs         cores, fontes, ícones e peças das telas (do design)
  Render/Bit.cs          o Bit e as animações (do design)
  Render/Screens.cs      as telas do display, com as medidas do design
  Voice/VoiceListener.cs voz offline (Vosk + NAudio)
  UI/                    janela de configurações e menu da bandeja
```

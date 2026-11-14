# CamBar para Windows

App de bandeja do sistema para Windows que mostra ao vivo as câmeras IP (RTSP) da sua rede. Um clique no ícone perto do relógio abre um painel com a imagem da câmera; dali você troca de câmera, abre uma janela expandida ou uma visão geral em grade.

Este é o port nativo para Windows do [CamBar para macOS](https://github.com/LucasAlt40/CamBar). A lógica (RTSP via ffmpeg, MJPEG, ONVIF/PTZ, diagnóstico de erros) foi portada de Swift para C#, e a interface foi refeita em WPF.

Feito para câmeras genéricas (como as que usam o app Yoosee), que costumam ter um app de celular ruim e nenhum cliente para desktop.

> **Status:** primeira versão do port. O núcleo (`CamBar.Core`) tem 83 verificações automáticas que passam, e o app inteiro compila. A interface WPF ainda **não foi testada em uma máquina Windows real**: espere ajustes visuais e de comportamento nas primeiras execuções. O CI (`.github/workflows/build.yml`) compila, roda as verificações e gera o executável a cada push.

## Recursos

- **Bandeja do sistema:** ícone de câmera com um ponto verde quando alguma câmera está online (o número aparece na dica ao passar o mouse). Clique abre o painel; clique direito abre o menu.
- **Painel rápido:** imagem ao vivo da câmera selecionada e abas para trocar de câmera sem reconectar. Fecha ao clicar fora ou com `Esc`.
- **Janela expandida:** vídeo grande com telemetria real (resolução, FPS e codec), seletor HD/SD, som, foto e tela cheia (`F11`). A janela mantém a proporção 16:9 e o cabeçalho aparece ao passar o mouse.
- **Controle PTZ:** direcional, zoom e joystick, por ONVIF. Só aparece se a câmera for motorizada.
- **Câmera fixada:** mini-janela flutuante com uma câmera em um dos quatro cantos da tela, acima das outras janelas e fora do `Alt+Tab`. O tamanho acompanha a tela (cerca de 1/5 da largura, de 280 a 560 px lógicos, sempre 16:9) ou pode ser ajustado arrastando a alça no canto interno; clique duas vezes na alça para voltar ao automático. Funciona com vários monitores e escalas de DPI diferentes. Câmera, canto, monitor e tamanho são lembrados ao reabrir o app.
- **Visão geral:** grade com todas as câmeras, estados de conexão e botão de reconectar.
- **Configuração:** janela responsiva com pré-visualização ao vivo e teste de sinal.
- **Procurar câmeras na rede:** com a sua permissão, faz uma busca ONVIF (WS-Discovery) e testa as portas RTSP 554 e 8554 da sua sub-rede local (até 254 endereços). Cada aparelho encontrado pode ser adicionado com IP e portas já preenchidos. Nada sai da rede local e nenhuma senha é enviada.
- **Baixo consumo:** as câmeras ficam conectadas em modo espera quando o painel está fechado, e vão para espera também com a sessão bloqueada ou o PC suspenso (ver [Desempenho](#desempenho)).
- **Segurança:** senhas guardadas no Gerenciador de Credenciais do Windows.

## Requisitos

- Windows 10 (1809 ou superior) ou Windows 11, 64 bits
- [ffmpeg](https://ffmpeg.org), que decodifica o stream:

```powershell
winget install Gyan.FFmpeg
```

O CamBar procura o `ffmpeg.exe` ao lado do app, em `%LOCALAPPDATA%\Microsoft\WinGet\Links`, no Chocolatey, no Scoop, em `C:\Program Files\ffmpeg\bin`, em `C:\ffmpeg\bin` e no `PATH`. Se instalou de outro jeito, basta copiar o `ffmpeg.exe` para a pasta do `CamBar.exe`.

O executável publicado é autocontido: não precisa instalar o .NET.

## Baixar

Baixe o `CamBar.exe` da [página de releases](https://github.com/LucasAlt40/CamBar-Windows/releases/latest), instale o ffmpeg e execute. Para saber se há versão nova, use o botão de atualizações no topo do painel (ou **Buscar atualizações** no menu do ícone). O Windows SmartScreen pode avisar que o app não é assinado: clique em **Mais informações** → **Executar assim mesmo**.

## Compilar e executar

Precisa do [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (ou mais novo) no Windows.

```powershell
dotnet run --project src/CamBar.App
```

Para gerar um único `CamBar.exe` autocontido:

```powershell
./scripts/publish.ps1
```

O resultado fica em `publish/CamBar.exe`. Para ficar disponível sempre, coloque um atalho dele na pasta de inicialização (`Win+R` → `shell:startup`).

O projeto do app também compila no macOS e no Linux (`dotnet build`), só para checar o código e o XAML; a interface só roda no Windows.

## Como usar

1. Clique no ícone da câmera na bandeja (se não aparecer, procure na seta `^` ao lado do relógio e arraste para a barra).
2. Abra o ícone de engrenagem e cadastre a câmera (veja abaixo).
3. Use **Testar Sinal** para confirmar que a câmera responde na porta, depois **Salvar**.
4. Volte ao painel: a imagem aparece em alguns segundos.

Para fixar uma câmera na tela, clique no ícone de alfinete (no painel, na janela expandida ou na visão geral) e escolha o canto. Passe o mouse sobre a mini-janela para trocar de canto, abrir em janela ou desafixar; clicar nela também abre a janela da câmera.

No painel, o botão de expandir abre a janela da câmera, o ícone de grade abre a visão geral e o botão **Sempre ativo** controla se as câmeras continuam conectadas com o painel fechado.

### Campos da câmera

| Campo | O que colocar |
|---|---|
| Nome | Como você quer chamar a câmera. |
| IP da câmera | Endereço local, por exemplo `192.168.1.10`. |
| Porta RTSP | Normalmente `554`. |
| Caminho RTSP | O trecho depois da porta, por exemplo `/onvif1`. |
| Porta ONVIF (PTZ) | Opcional. Se vazio, o app tenta `5000`, `80` e `8080`. |
| Usuário e senha | As credenciais do RTSP da câmera. |
| Transporte | Automático, UDP, TCP ou HTTP. Se não conectar, tente TCP. |

Senhas com caracteres especiais (`@`, `:`, `/`) funcionam sem ajuste: o app converte antes de montar a URL.

### Câmeras Yoosee e genéricas

Muitas câmeras só aceitam RTSP depois de ativá-lo no app do fabricante (procure por RTSP ou ONVIF nas configurações da câmera). Ao ativar, o app costuma mostrar o usuário e a senha do RTSP, que podem ser diferentes dos da sua conta. Caminhos comuns:

| Caminho | Uso |
|---|---|
| `/onvif1` | stream principal |
| `/onvif2` | stream secundário |
| `/live/ch0` e `/live/ch1` | modelos mais antigos |

O endereço completo fica assim: `rtsp://USUARIO:SENHA@IP:554/onvif1`. Se for testar no VLC, troque cada `@` da senha por `%40`.

## O que o app faz e não faz

O CamBar só recebe o que a câmera expõe por padrões abertos. Recursos que passam pelo protocolo fechado do fabricante não funcionam:

| Recurso | Funciona? |
|---|---|
| Vídeo ao vivo | Sim (RTSP) |
| Som da câmera | Sim, se o stream trouxer áudio |
| Foto | Sim, salva em `Imagens\CamBar` |
| Mover a câmera (PTZ) | Só se a câmera for motorizada e responder ao ONVIF |
| Falar pela câmera, luz, sirene | Não |
| Gravações e cartão SD | Não |
| Notificações de movimento | Não |

A câmera envia no máximo os FPS dela (por exemplo 15); o app não aumenta isso.

### Diferenças em relação ao macOS

- A câmera fixada fica acima das outras janelas, mas o Windows não tem o equivalente público de "todos os Spaces": ela aparece só na área de trabalho virtual em que está.
- A decodificação usa `-hwaccel auto` (D3D11/DXVA2 quando disponível) no lugar do VideoToolbox.
- O ícone da bandeja mostra um ponto de status e a contagem de câmeras online vai na dica, em vez do texto "N ON" da barra de menus.
- Janelas normais usam a barra de título do Windows (escura), em vez da barra transparente do macOS.

## Desempenho

O vídeo passa por um processo ffmpeg por câmera, que decodifica (com aceleração de hardware quando o Windows oferece) e entrega quadros JPEG ao app. A qualidade muda conforme o uso:

| Situação | Resolução | Quadros |
|---|---|---|
| Painel aberto | 720 px | todos os que a câmera envia |
| Janela expandida (HD) | 1280 px | todos os que a câmera envia |
| Modo espera (painel fechado) | 480 px | só quadros-chave, cerca de 1 por segundo |

- Com **Sempre ativo** ligado (padrão), as câmeras ficam conectadas mesmo com o painel fechado, então trocar de aba e abrir o painel é instantâneo.
- Desligado, as câmeras desconectam 60 segundos depois que o painel fecha.
- O status online vem de uma conexão TCP simples à porta de cada câmera, a cada 20 segundos. Isso só indica que a câmera responde na rede, não que o vídeo funciona.
- Os processos do ffmpeg ficam amarrados ao CamBar por um Job Object do Windows: se o app travar ou for encerrado à força, o sistema também encerra o ffmpeg.

## Segurança e privacidade

- As senhas ficam no Gerenciador de Credenciais do Windows (Painel de Controle → Gerenciador de Credenciais → Credenciais do Windows, entradas `CamBar/<id>`). As câmeras cadastradas (sem a senha) ficam em `%LOCALAPPDATA%\CamBar\cameras.json`.
- O ffmpeg recebe a URL com usuário e senha como argumento, então outros processos do mesmo PC podem vê-la enquanto o vídeo roda.
- O app só acessa a rede local; ele não envia nada para a internet.
- As câmeras ficam acessíveis a qualquer aparelho da sua rede. Use senhas fortes e, se o roteador tiver, isole a rede das câmeras.
- O formato de `cameras.json` é o mesmo do app de macOS, então dá para copiar a lista (as senhas não vão junto).

## Solução de problemas

O app mostra o motivo no lugar do vídeo. O ffmpeg grava o detalhe, com as credenciais mascaradas, em `%LOCALAPPDATA%\CamBar\Logs\CamBar.log`, as respostas do PTZ em `CamBar-ptz.log` e erros inesperados do próprio app em `CamBar-app.log`, na mesma pasta.

| Mensagem | Causa provável |
|---|---|
| A câmera não aceitou o RTSP (405) | A porta não fala RTSP. Ative RTSP/ONVIF no app da câmera. |
| Usuário ou senha incorretos (401/403) | Credenciais erradas. Use as que o app do fabricante mostra para o RTSP. |
| Caminho RTSP não encontrado (404) | Caminho errado. Teste `/onvif1`, `/onvif2` ou `/live/ch0`. |
| 400 Bad Request | Em câmeras genéricas, costuma ser usuário ou caminho incorreto. |
| Câmera inacessível | IP errado, câmera desligada ou fora da rede. O Firewall do Windows só afeta conexões de entrada, então raramente é o motivo. |
| ffmpeg não encontrado | Instale com `winget install Gyan.FFmpeg` ou copie o `ffmpeg.exe` para a pasta do app. |

## Estrutura do projeto

A solução tem três projetos:

- **`CamBar.Core`** (.NET 8, multiplataforma): lógica sem interface (modelos, comando do ffmpeg, leitura de MJPEG e do log, classificação de erros, ONVIF, persistência). Não depende de WPF nem de Windows.
- **`CamBar.App`** (.NET 8 Windows, WPF): o app, que usa o `CamBar.Core`.
- **`CamBar.Checks`**: verificações do `CamBar.Core`, como executável simples (sem framework de testes):

```powershell
dotnet run --project src/CamBar.Checks
```

```
src/CamBar.Core/
  Models/Camera.cs                 Câmera e transporte RTSP (JSON compatível com o app de macOS)
  Models/PinCorner.cs              Cantos e layout adaptativo da câmera fixada
  Persistence/Persistence.cs       Lista de câmeras e preferências em JSON
  Security/CredentialStore.cs      Interface das senhas
  Network/Reachability.cs          Teste de conexão TCP
  Logging/FileLog.cs               Log com credenciais mascaradas e tamanho limitado
  Streaming/                       Perfis, comando do ffmpeg, MJPEG, telemetria e erros
  Audio/PcmConverter.cs            Conversão do áudio
  Onvif/Onvif.cs                   Mensagens SOAP, WS-Security e cliente PTZ
src/CamBar.App/
  App.xaml, AppEnvironment.cs      Entrada e ambiente de dependências
  TrayController.cs, WindowRouter.cs  Ícone da bandeja e janelas
  Services/                        Credential Manager, lista de câmeras, status online, áudio, PTZ e fotos
  Streaming/                       Processo ffmpeg, stream de uma câmera e pool compartilhado
  Pinning/                         Câmera fixada: controlador e janela flutuante
  Controls/                        Vídeo, chips de status, PTZ e menu suspenso
  Views/                           Painel, janela expandida, visão geral e configurações
  Interop/                         Chamadas Win32 (credenciais, Job Object, DWM, DPI)
  Theme/                           Cores, estilos e construtores de interface
```

### Arquitetura

- **Injeção de dependências:** `AppEnvironment` cria os serviços uma vez e os entrega às janelas; a navegação passa por `IWindowRouter`.
- **Pool com empréstimo (lease):** cada view que mostra uma câmera pega um `StreamLease` do `StreamPool`, que mantém um único ffmpeg por câmera e escolhe o perfil (espera, normal ou HD) conforme quem está olhando.
- **Interface em código:** as janelas são montadas em C# (com `Theme.xaml` só para estilos de controles), o que mantém tudo verificado pelo compilador.
- **Concorrência:** estado no thread da interface; leitura e decodificação dos quadros fora dele, entregando só o quadro mais novo.
- **Interfaces:** repositório, credenciais, preferências e teste de conexão são interfaces, com versões em memória para as verificações.

## Créditos

- O ícone `cctv` é da biblioteca [Lucide](https://lucide.dev) (licença ISC).
- A decodificação de vídeo é feita pelo [FFmpeg](https://ffmpeg.org).
- O áudio usa o [NAudio](https://github.com/naudio/NAudio) (licença MIT).

## Licença

[MIT](LICENSE)

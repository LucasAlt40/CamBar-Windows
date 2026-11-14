using System.Text.Json;
using CamBar.Core;

// Self-checks for CamBar.Core. Run with `dotnet run --project src/CamBar.Checks`.
// Plain executable (no test framework) so it also runs on a machine with only the SDK.

var failures = 0;
var total = 0;

void Check(bool condition, string message, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
{
    total++;
    if (condition) return;
    failures++;
    Console.WriteLine($"  ✗ {message} (linha {line})");
}

void Suite(string name, Action body)
{
    Console.WriteLine($"• {name}");
    try { body(); }
    catch (Exception ex) { failures++; Console.WriteLine($"  ✗ erro inesperado: {ex}"); }
}

byte[] Bytes(params int[] values) => values.Select(v => (byte)v).ToArray();
bool Same(IEnumerable<byte[]> a, params byte[][] b) => a.Count() == b.Length && a.Zip(b).All(p => p.First.SequenceEqual(p.Second));

Suite("Camera", () =>
{
    var cam = new Camera { Name = "Garagem", Host = "192.168.1.10", Path = "onvif1", Username = "admin" };
    Check(cam.NormalizedPath == "/onvif1", "caminho ganha a barra inicial");
    Check(cam.RtspUrl("@ab:c/d") == "rtsp://admin:%40ab%3Ac%2Fd@192.168.1.10:554/onvif1", "senha com @ : / é codificada");
    Check(cam.RtspUrl("") == "rtsp://admin@192.168.1.10:554/onvif1", "sem senha mantém só o usuário");
    Check(new Camera { Host = "1.2.3.4" }.RtspUrl("x") == "rtsp://1.2.3.4:554/", "sem usuário não envia credenciais");
    Check(cam.DisplayUrl == "rtsp://192.168.1.10:554/onvif1", "DisplayUrl não mostra credenciais");

    // data written by the macOS version has the same shape
    const string legacy = """[{"id":"7C1C3A5E-2F14-4E0E-9C3C-1B8D4F6A9E21","name":"A","host":"h","port":554,"path":"/","username":"u","transport":null}]""";
    var decoded = JsonSerializer.Deserialize<List<Camera>>(legacy, CameraJson.Options)!;
    Check(decoded[0].Transport == RtspTransport.Auto, "dado antigo com transport null vira automático");
    Check(decoded[0].OnvifPort is null, "dado antigo sem onvifPort");
    Check(decoded[0].Id == Guid.Parse("7C1C3A5E-2F14-4E0E-9C3C-1B8D4F6A9E21"), "id em maiúsculas é lido");

    var roundTrip = JsonSerializer.Deserialize<Camera>(JsonSerializer.Serialize(cam, CameraJson.Options), CameraJson.Options);
    Check(roundTrip == cam, "codifica e decodifica sem perda");
    var tcp = cam with { Transport = RtspTransport.Tcp };
    Check(JsonSerializer.Serialize(tcp, CameraJson.Options).Contains("\"transport\": \"tcp\""), "transporte salvo em texto");
});

Suite("Transporte e comando do ffmpeg", () =>
{
    Check(RtspTransport.Auto.FfmpegArguments().Length == 0, "automático não força transporte");
    Check(RtspTransport.Tcp.FfmpegArguments().SequenceEqual(["-rtsp_transport", "tcp"]), "tcp forçado");

    var standby = FFmpeg.VideoArguments("rtsp://x", RtspTransport.Udp, StreamProfile.Standby);
    Check(standby.Contains("nokey"), "espera decodifica só quadros-chave");
    Check(standby.Contains("scale='min(480,iw)':-2"), "espera usa 480 px");
    var hd = FFmpeg.VideoArguments("rtsp://x", RtspTransport.Auto, StreamProfile.Hd);
    Check(!hd.Contains("nokey"), "HD decodifica todos os quadros");
    Check(hd.Contains("scale='min(1280,iw)':-2"), "HD usa 1280 px");
    Check(hd[^1] == "pipe:1", "saída no stdout");
    Check(FFmpeg.Locate(p => p.EndsWith(@"chocolatey\bin\ffmpeg.exe") || p.EndsWith("chocolatey/bin/ffmpeg.exe")) is not null,
          "procura o ffmpeg nas pastas usuais do Windows");
    Check(FFmpeg.Locate(_ => false) is null, "sem ffmpeg devolve nulo");
});

Suite("MjpegParser", () =>
{
    var frameA = Bytes(0xFF, 0xD8, 0x01, 0x02, 0xFF, 0xD9);
    var frameB = Bytes(0xFF, 0xD8, 0x03, 0xFF, 0xD9);
    var parser = new MjpegParser();
    Check(parser.Append([0x00, 0x11, .. frameA[..3]]).Count == 0, "quadro incompleto não sai");
    var output = parser.Append([.. frameA[3..], .. frameB]);
    Check(Same(output, frameA, frameB), "junta pedaços e separa dois quadros");

    var small = new MjpegParser(maxBufferSize: 4);
    small.Append(Bytes(0xFF, 0xD8, 1, 2, 3, 4, 5));
    Check(Same(small.Append(frameA), frameA), "buffer corrompido é descartado");

    // start marker split between chunks
    var split = new MjpegParser();
    Check(split.Append(Bytes(0x00, 0xFF)).Count == 0, "primeira metade do marcador");
    Check(Same(split.Append(Bytes(0xD8, 0x07, 0xFF, 0xD9)), Bytes(0xFF, 0xD8, 0x07, 0xFF, 0xD9)), "marcador inicial dividido");
});

Suite("Log do ffmpeg", () =>
{
    var parser = new FFmpegLogParser();
    var changed = parser.Consume("""
      Stream #0:0: Video: h264 (Main), 1 reference frame, yuvj420p(pc, bt709, progressive, left), 2304x1296, 15 fps, 15 tbr, 90k tbn
      Stream #0:1: Audio: pcm_alaw, 16000 Hz, mono, s16, 128 kb/s
      Stream #0:0: Video: mjpeg, 1 reference frame, yuvj420p(pc, bt709, progressive), 1280x720, q=2-31, 200 kb/s, 12 fps

    """);
    Check(changed, "telemetria encontrada");
    Check(parser.Info == new StreamInfo(2304, 1296, 15, "H.264 Main", true), "lê a entrada e ignora a saída mjpeg");
    Check(parser.Info?.Summary == "2304×1296 • 15 FPS • H.264 Main", "resumo da telemetria");

    var hevc = FFmpegLogParser.ParseVideo("Stream #0:0: Video: hevc (Main), yuv420p(tv), 1920x1080, 15 fps, 15 tbr");
    Check(hevc?.Codec == "H.265 Main" && hevc?.Width == 1920, "H.265");

    var split = new FFmpegLogParser();
    split.Consume("  Stream #0:1: Audio: pcm_alaw, 16000 Hz, mono\n  Stream #0:0: Video: hevc (Main), yuv420p(tv), 19");
    Check(split.Info?.HasVideo == false, "linha cortada ainda não conta");
    split.Consume("20x1080, 15 fps, 15 tbr\n");
    Check(split.Info?.Resolution == "1920×1080" && split.Info?.HasAudio == true, "junta linha dividida em dois pedaços");

    parser.Consume("Error opening input files: Server returned 400 Bad Request");
    parser.Finish();
    Check(StreamFailure.Classify(parser.LastError) == StreamFailure.BadRequest, "guarda a última linha de erro");
});

Suite("Classificação de erros", () =>
{
    Check(StreamFailure.Classify("Server returned 4XX Client Error, but not one of 40{0,1,3,4}") == StreamFailure.NotRtsp, "405");
    Check(StreamFailure.Classify("method DESCRIBE failed: 401 Unauthorized") == StreamFailure.Unauthorized, "401");
    Check(StreamFailure.Classify("Server returned 404 Not Found") == StreamFailure.PathNotFound, "404");
    Check(StreamFailure.Classify("Connection to tcp://1.2.3.4:554 failed: Connection refused") == StreamFailure.Unreachable, "recusado");
    Check(StreamFailure.Classify("algo estranho") == StreamFailure.Unknown, "desconhecido");
});

Suite("Mascaramento do log", () =>
{
    Check(FileLog.Mask("rtsp://admin:%40x@192.168.1.10:554/onvif1") == "rtsp://***@192.168.1.10:554/onvif1", "esconde credenciais");
    Check(FileLog.Mask("rtsp://192.168.1.10:554/onvif1") == "rtsp://192.168.1.10:554/onvif1", "URL sem credenciais intacta");

    var dir = Path.Combine(Path.GetTempPath(), "cambar-checks-" + Guid.NewGuid());
    try
    {
        var log = new FileLog("t", maxBytes: 200, directory: dir);
        for (var i = 0; i < 50; i++) log.Append($"linha {i} rtsp://u:p@h/x", tag: "cam");
        var text = File.ReadAllText(log.Path);
        Check(new FileInfo(log.Path).Length <= 400, "o arquivo não cresce sem limite");
        Check(text.Contains("linha 49") && !text.Contains("u:p@"), "guarda o mais novo e mascara a senha");
    }
    finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
});

Suite("PcmConverter", () =>
{
    var converter = new PcmConverter();
    Check(converter.Convert(Bytes(0x00)).Length == 0, "meio sample fica guardado");
    Check(converter.Convert(Bytes(0x80, 0xFF, 0x7F)).SequenceEqual([-1f, 32767f / 32768f]), "junta o byte guardado");
    Check(converter.Convert([]).Length == 0, "chunk vazio");
});

Suite("ONVIF", () =>
{
    // Example from the ONVIF Application Programmer's Guide.
    var digest = Onvif.PasswordDigest(Convert.FromBase64String("LKqI6G/AikKCQrN0zqZFlg=="), "2010-09-16T07:50:45Z", "userpassword");
    Check(digest == "tuOSpGlFlIXsozq4HFNeeGeFLEI=", "digest WS-Security");

    const string caps = """
    <tt:Media><tt:XAddr>http://10.0.0.9:5000/onvif/media</tt:XAddr></tt:Media>
    <tt:PTZ> <tt:XAddr>http://10.0.0.9:5000/onvif/ptz</tt:XAddr></tt:PTZ>
    """;
    Check(Onvif.ServiceAddress("PTZ", caps) == "http://10.0.0.9:5000/onvif/ptz", "endereço do PTZ");
    Check(Onvif.Rewrite("http://10.0.0.9:5000/onvif/ptz", "192.168.1.10")?.AbsoluteUri == "http://192.168.1.10:5000/onvif/ptz",
          "troca o host interno pelo configurado");

    const string profiles = """
    <trt:Profiles token="fixed"><tt:Name>a</tt:Name></trt:Profiles>
    <trt:Profiles fixed="true" token="moving"><tt:PTZConfiguration token="p"/></trt:Profiles>
    """;
    Check(Onvif.PtzProfileToken(profiles) == "moving", "escolhe o perfil com PTZ");
    Check(Onvif.PtzProfileToken("<trt:Profiles token=\"x\"></trt:Profiles>") is null, "câmera fixa");
    Check("a<b>&\"'".XmlEscaped() == "a&lt;b&gt;&amp;&quot;&apos;", "escapa XML");
    Check(Onvif.ContinuousMove("t", 0.5, -0.25, 0).Contains("x=\"0.5\" y=\"-0.25\""), "velocidades usam ponto decimal");
});

Suite("Robustez do streaming", () =>
{
    var parser = new FFmpegLogParser();
    for (var i = 0; i < 5_000; i++) parser.Consume("frame=  373 fps=0.2 q=8.0 size=    4507KiB time=00:24:59.60    \r");
    parser.Consume("Stream #0:0: Video: h264 (Main), yuv420p, 1920x1080, 15 fps, 15 tbr\r");
    Check(parser.Info?.Width == 1920, "linhas terminadas em \\r são processadas e não acumulam");
    Check(FFmpeg.VideoArguments("rtsp://x", RtspTransport.Auto, StreamProfile.Normal).Contains("-nostats"), "ffmpeg sem linha de progresso");
    Check(FFmpeg.IsNoise("[mjpeg @ 0x1] Application provided invalid, non monotonically increasing dts"), "aviso repetido do muxer não vai para o log");

    // big frame arriving in small pieces, with the end marker split between two chunks
    var mjpeg = new MjpegParser();
    var frame = new byte[] { 0xFF, 0xD8 }.Concat(Enumerable.Repeat((byte)0x11, 10_000)).Concat(new byte[] { 0xFF, 0xD9 }).ToArray();
    var found = new List<byte[]>();
    var offset = 0;
    foreach (var size in new[] { 700, 3_000, 6_000, 303, 1 })   // sums to 10,004; the last one cuts between 0xFF and 0xD9
    {
        var length = Math.Min(size, frame.Length - offset);
        found.AddRange(mjpeg.Append(frame.AsSpan(offset, length)));
        offset += size;
    }
    Check(Same(found, frame), "remonta o frame mesmo com o marcador final dividido");
    found = mjpeg.Append([.. frame, .. frame[..5]]);
    Check(Same(found, frame), "frame seguinte e começo de outro no mesmo pedaço");
});

Suite("Câmera fixada na tela", () =>
{
    var screen = new RectD(0, 0, 1440, 800);   // work area: the taskbar is already excluded
    var size = PinLayout.Size(screen);
    Check(size.Width / size.Height is > 1.77 and < 1.78, "mantém 16:9");
    Check(size.Width == 288, "usa um quinto da largura");
    Check(size.Height <= screen.Height / 3, "nunca passa de um terço da altura");

    var frames = Enum.GetValues<PinCorner>().Select(c => PinLayout.Frame(c, screen)).ToList();
    Check(frames.All(screen.Contains), "fica dentro da área visível");
    var bottomLeft = PinLayout.Frame(PinCorner.BottomLeading, screen);
    Check(bottomLeft.X == 16 && bottomLeft.MaxY == 784, "inferior esquerdo");
    Check(PinLayout.Frame(PinCorner.TopTrailing, screen).MaxX == 1424, "superior direito encosta na margem");
    Check(PinLayout.Frame(PinCorner.TopLeading, screen).Y == 16, "superior esquerdo logo abaixo da margem");

    Check(PinLayout.Size(new RectD(0, 0, 800, 600)).Width == 280, "tela pequena: mínimo de 280");
    Check(PinLayout.Size(new RectD(0, 0, 5120, 2800)).Width == 560, "tela grande: máximo de 560");
    var portrait = new RectD(1440, 0, 1080, 1920);   // monitor in portrait
    var portraitFrame = PinLayout.Frame(PinCorner.BottomTrailing, portrait);
    Check(portrait.Contains(portraitFrame) && portraitFrame.Width == 280, "monitor vertical: cabe e usa o mínimo");

    Check(PinLayout.Size(screen, 900).Width == 900, "largura escolhida pelo usuário");
    Check(PinLayout.Size(screen, 50).Width == PinLayout.MinWidth, "não fica menor que o mínimo");
    Check(PinLayout.Size(screen, 5_000).Height <= screen.Height - 32, "não passa da tela");
    Check(PinLayout.Size(screen, null) == size, "sem largura escolhida volta ao automático");
    var grown = PinLayout.Frame(PinCorner.BottomTrailing, screen, 600);
    Check(grown.MaxX == 1424 && grown.MaxY == 784, "cresce mantendo o canto fixado");
    // y grows downward on Windows: dragging up-left from the bottom-right corner grows the picture
    var diagonal = PinLayout.WidthChange(-160, -90, PinCorner.BottomTrailing);
    Check(Math.Abs(diagonal - 160) < 0.01, "arrastar na diagonal 16:9 acompanha o ponteiro");
    Check(PinLayout.WidthChange(100, 0, PinCorner.BottomTrailing) < 0, "puxar para o canto fixado diminui");
    Check(PinLayout.WidthChange(100, 50, PinCorner.TopLeading) > 0, "canto superior esquerdo cresce para baixo e à direita");
    Check(PinCorner.BottomTrailing.Opposite() == PinCorner.TopLeading, "alça fica no canto oposto");
    Check(PinLayout.Size(new RectD(0, 0, 600, 300)).Height <= 100, "tela baixa limita a altura");
});

Suite("Persistência", () =>
{
    var repo = new InMemoryCameraRepository();
    repo.Save([new Camera { Name = "X" }]);
    Check(repo.Load().Select(c => c.Name).SequenceEqual(["X"]), "salva e carrega");

    var creds = new InMemoryCredentialStore();
    var id = Guid.NewGuid();
    creds.SetPassword("s", id);
    Check(creds.GetPassword(id) == "s", "guarda senha");
    creds.SetPassword("", id);
    Check(creds.GetPassword(id).Length == 0, "senha vazia remove");

    var dir = Path.Combine(Path.GetTempPath(), "cambar-checks-" + Guid.NewGuid());
    try
    {
        var file = new JsonFileCameraRepository(Path.Combine(dir, "cameras.json"));
        Check(file.Load().Count == 0, "arquivo inexistente vira lista vazia");
        var cam = new Camera { Name = "Portão", Host = "10.0.0.2", Transport = RtspTransport.Tcp, OnvifPort = 8899 };
        file.Save([cam]);
        Check(file.Load().SequenceEqual([cam]), "arquivo JSON mantém todos os campos");
        File.WriteAllText(Path.Combine(dir, "cameras.json"), "{ quebrado");
        Check(file.Load().Count == 0, "arquivo corrompido não derruba o app");

        var settingsPath = Path.Combine(dir, "settings.json");
        var settings = new JsonSettings(settingsPath);
        settings.Set("pinnedCorner", "topLeading");
        settings.Set("keepConnected", false);
        settings.Set("pinnedWidth", 412.5);
        var reread = new JsonSettings(settingsPath);
        Check(reread.GetString("pinnedCorner") == "topLeading" && reread.GetBool("keepConnected") == false
              && reread.GetDouble("pinnedWidth") == 412.5, "preferências sobrevivem a reiniciar");
        reread.Remove("pinnedCorner");
        Check(new JsonSettings(settingsPath).GetString("pinnedCorner") is null, "remover apaga a chave");
    }
    finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
});

Suite("Atualizações", () =>
{
    Check(UpdateChecker.Normalize("v1.2.3") == "1.2.3", "tira o v da tag");
    Check(UpdateChecker.Normalize("0.2.0+7d7f67fabc") == "0.2.0", "ignora o hash do build");
    Check(UpdateChecker.IsNewer("0.10.0", "0.9.0"), "compara números, não texto");
    Check(UpdateChecker.IsNewer("v0.2.0", "0.1.0+abc"), "tag nova é mais nova que a versão atual");
    Check(!UpdateChecker.IsNewer("1.0", "1.0.0"), "1.0 e 1.0.0 são iguais");
    Check(!UpdateChecker.IsNewer("0.1.0", "0.2.0"), "versão antiga não é atualização");

    const string json = """{"tag_name":"v0.3.0","html_url":"https://github.com/x/y/releases/tag/v0.3.0","body":"Notas"}""";
    var release = UpdateChecker.Parse(json);
    Check(release == new ReleaseInfo("0.3.0", "https://github.com/x/y/releases/tag/v0.3.0", "Notas"), "lê a release do GitHub");
    Check(UpdateChecker.Parse("{}") is null && UpdateChecker.Parse("não é json") is null, "resposta inválida não quebra");
    Check(UpdateChecker.Compare(release, "0.2.0").Status == UpdateStatus.Available, "avisa quando há versão nova");
    Check(UpdateChecker.Compare(release, "0.3.0").Status == UpdateStatus.UpToDate, "mesma versão está atualizada");
    Check(UpdateChecker.Compare(null, "0.3.0").Status == UpdateStatus.NoReleases, "sem releases publicadas");
});

Console.WriteLine(failures == 0 ? $"\n{total} checks passaram." : $"\n{failures} de {total} checks falharam.");
return failures == 0 ? 0 : 1;

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Netcheck;

public partial class MainWindow : Window
{
    private static readonly HttpClient Http = new HttpClient();
    private DispatcherTimer _timer;
    private bool _running;
    private bool _updateChecked;
    private bool _isDarkMode;
    private bool _speedtestRunning;
    private string? _lastIp;
    private DateTime _lastMailSent = DateTime.MinValue;
    private static readonly TimeSpan MailCooldown = TimeSpan.FromMinutes(10);
    private const string SettingsFileName = "mailsettings.dat";
    private const string WindowStateFileName = "windowstate.json";
    private const string UpdateManifestUrl = "https://raw.githubusercontent.com/Anthanaab/Netcheck/master/update.json";
    private const string UpdateManifestFallbackUrl = "https://raw.githubusercontent.com/Anthanaab/Netcheck/refs/heads/master/update.json";
    private const string SpeedtestDownloadBaseUrl = "https://speed.polylabs.ch/backend/garbage.php";
    private const string SpeedtestUploadBaseUrl = "https://speed.polylabs.ch/backend/empty.php";
    private const string SpeedtestPingUrl = "https://speed.polylabs.ch/backend/empty.php";
    private const int SpeedtestDownloadStreams = 6;
    private const int SpeedtestUploadStreams = 2;
    private const int SpeedtestPingSamples = 5;
    private const int SpeedtestDownloadSamples = 3;
    private const int SpeedtestUploadSamples = 3;
    private const int SpeedtestPingTimeoutSeconds = 3;
    private const int SpeedtestDownloadDurationSeconds = 10;
    private const int SpeedtestDownloadTimeoutSeconds = 15;
    private const int SpeedtestUploadBytesPerStream = 10 * 1024 * 1024;
    private const int SpeedtestUploadTimeoutSeconds = 25;

    public MainWindow()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _timer.Tick += async (_, _) => await RunCheckAsync();
        InitializeComponent();
        LoadMailSettings();
        VersionText.Text = $"v{GetCurrentVersion():0.0.0}";
        BindThemeForegrounds();
        ApplyTheme(false);
        Loaded += (_, _) => RestoreWindowPosition();
        Closing += (_, _) => SaveWindowPosition();
        Loaded += async (_, _) => await CheckForUpdatesAsync();
    }

    private async void CheckButton_Click(object sender, RoutedEventArgs e)
    {
        await RunCheckAsync();
    }

    private void AutoCheck_Changed(object sender, RoutedEventArgs e)
    {
        ApplyInterval();
        if (AutoCheck.IsChecked == true)
        {
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    private void IntervalBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        ApplyInterval();
    }

    private async Task RunCheckAsync()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        SetBusy(true);
        StatusText.Text = "Vérification en cours...";
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        ConnectionText.Text = "—";
        ConnectionText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        PingText.Text = "—";
        PingText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        IpText.Text = "—";
        IpText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        LocationText.Text = "—";
        LocationText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");

        try
        {
            bool connected = await IsConnectedAsync(TimeSpan.FromSeconds(3));
            if (connected)
            {
                ConnectionText.Text = "OK";
                ConnectionText.Foreground = Brushes.ForestGreen;

                long? pingMs = await PingAsync("1.1.1.1", TimeSpan.FromSeconds(2));
                if (pingMs.HasValue)
                {
                    PingText.Text = $"{pingMs.Value} ms";
                    if (pingMs.Value <= 50)
                    {
                        PingText.Foreground = Brushes.ForestGreen;
                    }
                    else if (pingMs.Value <= 150)
                    {
                        PingText.Foreground = Brushes.DarkOrange;
                    }
                    else
                    {
                        PingText.Foreground = Brushes.Firebrick;
                    }
                }
                else
                {
                    PingText.Text = "erreur";
                    PingText.Foreground = Brushes.DarkOrange;
                }

                string? ip = await PublicIPAsync(TimeSpan.FromSeconds(5));
                if (string.IsNullOrWhiteSpace(ip))
                {
                    IpText.Text = "erreur";
                    IpText.Foreground = Brushes.DarkOrange;
                    StatusText.Text = "Connexion OK, IP indisponible";
                    StatusText.Foreground = Brushes.DarkOrange;
                }
                else
                {
                    IpText.Text = ip;
                    IpText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
                    StatusText.Text = "Connexion OK";
                    StatusText.Foreground = Brushes.ForestGreen;

                    string? location = await GeoLocationAsync(ip, TimeSpan.FromSeconds(5));
                    LocationText.Text = string.IsNullOrWhiteSpace(location) ? "inconnu" : location;
                    LocationText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");

                    if (NotifyEnabled.IsChecked == true)
                    {
                        await NotifyIfIpChangedAsync(ip);
                    }
                }
            }
            else
            {
                ConnectionText.Text = "NON";
                ConnectionText.Foreground = Brushes.Firebrick;
                IpText.Text = "?";
                IpText.Foreground = Brushes.Firebrick;
                PingText.Text = "?";
                PingText.Foreground = Brushes.Firebrick;
                StatusText.Text = "Pas d'accès Internet";
                StatusText.Foreground = Brushes.Firebrick;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Erreur: {ex.Message}";
            StatusText.Foreground = Brushes.Firebrick;
        }
        finally
        {
            LastCheckText.Text = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
            SetBusy(false);
            _running = false;
        }
    }

    private void SetBusy(bool busy)
    {
        CheckButton.IsEnabled = !busy;
    }

    private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyTheme(!_isDarkMode);
    }

    private void ApplyTheme(bool dark)
    {
        _isDarkMode = dark;

        SetBrushColor("WindowBackgroundBrush", dark ? "#111311" : "#F3F5F4");
        SetBrushColor("CardBackgroundBrush", dark ? "#1B1F1C" : "#FFFFFF");
        SetBrushColor("InputBackgroundBrush", dark ? "#151815" : "#FFFFFF");
        SetBrushColor("InputBorderBrush", dark ? "#2A322E" : "#C9D2CC");
        SetBrushColor("TextPrimaryBrush", dark ? "#F2F5F2" : "#1F1F1F");
        SetBrushColor("TextMutedBrush", dark ? "#A6B0AA" : "#606060");

        ThemeToggleButton.Content = dark ? "Mode clair" : "Mode sombre";
    }

    private void BindThemeForegrounds()
    {
        IpText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        LocationText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        LastCheckText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        VersionText.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        MailStatusText.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        SpeedPingText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        SpeedDownText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        SpeedUpText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        SpeedStatusText.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
    }

    private static void SetBrushColor(string key, string colorHex)
    {
        var color = (Color)ColorConverter.ConvertFromString(colorHex)!;
        Application.Current.Resources[key] = new SolidColorBrush(color);
    }

    private void ApplyInterval()
    {
        if (_timer == null || IntervalBox == null)
        {
            return;
        }

        if (!int.TryParse(IntervalBox.Text, out int seconds))
        {
            return;
        }

        if (seconds < 2)
        {
            seconds = 2;
        }
        else if (seconds > 300)
        {
            seconds = 300;
        }

        _timer.Interval = TimeSpan.FromSeconds(seconds);
    }

    private async void SpeedtestButton_Click(object sender, RoutedEventArgs e)
    {
        if (_speedtestRunning)
        {
            return;
        }

        _speedtestRunning = true;
        SpeedtestButton.IsEnabled = false;
        SpeedStatusText.Text = "Test en cours...";
        SpeedStatusText.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        SpeedPingText.Text = "—";
        SpeedDownText.Text = "—";
        SpeedUpText.Text = "—";

        try
        {
            double? pingMs = await AverageSamplesAsync(
                token => MeasurePingMsIcmpAsync(token),
                samples: SpeedtestPingSamples,
                perSampleTimeout: TimeSpan.FromSeconds(SpeedtestPingTimeoutSeconds),
                (index, total) => SpeedStatusText.Text = $"Test ping... ({index}/{total})");
            SpeedPingText.Text = pingMs.HasValue ? $"{pingMs.Value:0} ms" : "erreur";

            double? downMbps = await AverageSamplesAsync(
                token => MeasureDownloadMbpsParallelAsync(TimeSpan.FromSeconds(SpeedtestDownloadDurationSeconds), token, SpeedtestDownloadStreams),
                samples: SpeedtestDownloadSamples,
                perSampleTimeout: TimeSpan.FromSeconds(SpeedtestDownloadTimeoutSeconds),
                (index, total) => SpeedStatusText.Text = $"Test download... ({index}/{total})");
            SpeedDownText.Text = downMbps.HasValue ? $"{downMbps.Value:0.0} Mbps" : "erreur";

            double? upMbps = await AverageSamplesAsync(
                token => MeasureUploadMbpsParallelAsync(SpeedtestUploadBytesPerStream, token, SpeedtestUploadStreams),
                samples: SpeedtestUploadSamples,
                perSampleTimeout: TimeSpan.FromSeconds(SpeedtestUploadTimeoutSeconds),
                (index, total) => SpeedStatusText.Text = $"Test upload... ({index}/{total})");
            SpeedUpText.Text = upMbps.HasValue ? $"{upMbps.Value:0.0} Mbps" : "erreur";

            SpeedStatusText.Text = "Terminé";
        }
        catch (Exception ex)
        {
            SpeedStatusText.Text = $"Erreur: {ex.Message}";
        }
        finally
        {
            _speedtestRunning = false;
            SpeedtestButton.IsEnabled = true;
        }
    }

    private static async Task<double?> MeasurePingMsIcmpAsync(CancellationToken token)
    {
        try
        {
            using var ping = new Ping();
            PingReply reply = await ping.SendPingAsync("speed.polylabs.ch", 2000);
            return reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<double?> MeasureDownloadMbpsParallelAsync(TimeSpan duration, CancellationToken token, int streams)
    {
        using var durationCts = new CancellationTokenSource(duration);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, durationCts.Token);
        Task<long>[] tasks = new Task<long>[streams];
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < streams; i++)
        {
            tasks[i] = MeasureDownloadBytesAsync(linkedCts.Token);
        }

        long[] results = await Task.WhenAll(tasks);
        sw.Stop();
        long totalBytes = 0;
        foreach (long bytes in results)
        {
            totalBytes += bytes;
        }

        double seconds = sw.Elapsed.TotalSeconds;
        return seconds <= 0 ? null : totalBytes * 8d / seconds / 1_000_000d;
    }

    private static async Task<long> MeasureDownloadBytesAsync(CancellationToken token)
    {
        string url = AppendNoCacheParam(SpeedtestDownloadBaseUrl);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using HttpResponseMessage response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode)
        {
            return 0;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(token);
        byte[] buffer = new byte[64 * 1024];
        long total = 0;

        while (!token.IsCancellationRequested)
        {
            int read = await stream.ReadAsync(buffer, token);
            if (read <= 0)
            {
                break;
            }
            total += read;
        }

        return total;
    }

#pragma warning disable SYSLIB0014
    private static async Task<UploadResult?> MeasureUploadBytesAsync(int bytesToSend, CancellationToken token)
    {
        try
        {
            string url = AppendNoCacheParam(SpeedtestUploadBaseUrl);
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.AllowWriteStreamBuffering = false;
            request.SendChunked = false;
            request.ContentType = "application/octet-stream";
            request.ContentLength = bytesToSend;

            var sw = Stopwatch.StartNew();
            using (Stream reqStream = await request.GetRequestStreamAsync())
            {
                byte[] buffer = new byte[64 * 1024];
                int remaining = bytesToSend;
                while (remaining > 0 && !token.IsCancellationRequested)
                {
                    int chunk = Math.Min(buffer.Length, remaining);
                    Random.Shared.NextBytes(buffer.AsSpan(0, chunk));
                    await reqStream.WriteAsync(buffer.AsMemory(0, chunk), token);
                    remaining -= chunk;
                }
            }

            using (var response = (HttpWebResponse)await request.GetResponseAsync())
            using (var respStream = response.GetResponseStream())
            {
                if (respStream != null)
                {
                    await respStream.CopyToAsync(Stream.Null, token);
                }
            }

            sw.Stop();
            return sw.Elapsed.TotalSeconds <= 0 ? null : new UploadResult(bytesToSend, sw.Elapsed.TotalSeconds);
        }
        catch
        {
            return null;
        }
    }
#pragma warning restore SYSLIB0014

    private static async Task<double?> MeasureUploadMbpsParallelAsync(int bytesPerStream, CancellationToken token, int streams)
    {
        Task<UploadResult?>[] tasks = new Task<UploadResult?>[streams];
        for (int i = 0; i < streams; i++)
        {
            tasks[i] = MeasureUploadBytesAsync(bytesPerStream, token);
        }

        UploadResult?[] results = await Task.WhenAll(tasks);

        long totalBytes = 0;
        double maxSeconds = 0;
        foreach (UploadResult? result in results)
        {
            if (result.HasValue)
            {
                totalBytes += result.Value.Bytes;
                if (result.Value.Seconds > maxSeconds)
                {
                    maxSeconds = result.Value.Seconds;
                }
            }
        }

        if (totalBytes == 0 || maxSeconds <= 0)
        {
            return null;
        }

        return totalBytes * 8d / maxSeconds / 1_000_000d;
    }

    private static async Task<double?> AverageSamplesAsync(
        Func<CancellationToken, Task<double?>> sampler,
        int samples,
        TimeSpan perSampleTimeout,
        Action<int, int> onProgress)
    {
        double total = 0;
        int count = 0;

        for (int i = 1; i <= samples; i++)
        {
            onProgress(i, samples);
            using var cts = new CancellationTokenSource(perSampleTimeout);
            double? value = await sampler(cts.Token);
            if (value.HasValue)
            {
                total += value.Value;
                count++;
            }
        }

        return count == 0 ? null : total / count;
    }

    private static string AppendNoCacheParam(string baseUrl)
    {
        string separator = baseUrl.Contains("?") ? "&" : "?";
        string nonce = Random.Shared.NextDouble().ToString("0.################", CultureInfo.InvariantCulture);
        return baseUrl + separator + "n=" + nonce;
    }

    private readonly struct UploadResult
    {
        public long Bytes { get; }
        public double Seconds { get; }

        public UploadResult(long bytes, double seconds)
        {
            Bytes = bytes;
            Seconds = seconds;
        }
    }


    private void OpenSpeedtestButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://speed.polylabs.ch") { UseShellExecute = true });
    }

    private async Task CheckForUpdatesAsync()
    {
        if (_updateChecked)
        {
            return;
        }

        _updateChecked = true;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            HttpResponseMessage? response = await FetchUpdateResponseAsync(cts.Token);
            if (response == null)
            {
                return;
            }

            using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            UpdateInfo? update = await JsonSerializer.DeserializeAsync<UpdateInfo>(stream, cancellationToken: cts.Token);
            if (update == null || string.IsNullOrWhiteSpace(update.Version) || string.IsNullOrWhiteSpace(update.Url))
            {
                return;
            }

            Version current = GetCurrentVersion();
            if (!IsNewerVersion(update.Version, current))
            {
                return;
            }

            string notesBlock = string.IsNullOrWhiteSpace(update.Notes) ? "" : "\n\n" + update.Notes;
            string message = $"Une nouvelle version est disponible ({update.Version}).{notesBlock}\n\nVoulez-vous la telecharger et l'installer maintenant ?";
            if (MessageBox.Show(message, "Mise a jour", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes)
            {
                return;
            }

            using var downloadCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            string installerPath = await DownloadInstallerAsync(update.Url, downloadCts.Token);
            Process.Start(new ProcessStartInfo(installerPath)
            {
                UseShellExecute = true,
                Arguments = "/autostart"
            });
            Application.Current.Shutdown();
        }
        catch
        {
            // Ignore update failures.
        }
    }

    private static async Task<HttpResponseMessage?> FetchUpdateResponseAsync(CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UpdateManifestUrl);
        HttpResponseMessage response = await Http.SendAsync(request, token);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        response.Dispose();

        using var fallbackRequest = new HttpRequestMessage(HttpMethod.Get, UpdateManifestFallbackUrl);
        HttpResponseMessage fallbackResponse = await Http.SendAsync(fallbackRequest, token);
        if (fallbackResponse.IsSuccessStatusCode)
        {
            return fallbackResponse;
        }

        fallbackResponse.Dispose();
        return null;
    }

    private static async Task<string> DownloadInstallerAsync(string url, CancellationToken token)
    {
        string fileName = Path.GetFileName(new Uri(url).LocalPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "Netcheck-Setup.exe";
        }

        string destPath = Path.Combine(Path.GetTempPath(), fileName);
        using HttpResponseMessage response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        await using var file = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await stream.CopyToAsync(file, token);
        return destPath;
    }

    private static Version GetCurrentVersion()
    {
        return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);
    }

    private static bool IsNewerVersion(string latestVersion, Version current)
    {
        if (!Version.TryParse(latestVersion, out Version? latest))
        {
            return false;
        }

        return latest > current;
    }

    private static async Task<bool> IsConnectedAsync(TimeSpan timeout)
    {
        using var client = new TcpClient();
        Task connectTask = client.ConnectAsync("1.1.1.1", 53);
        Task delayTask = Task.Delay(timeout);
        Task completed = await Task.WhenAny(connectTask, delayTask);
        if (completed == delayTask)
        {
            return false;
        }

        try
        {
            await connectTask;
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string?> PublicIPAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.ipify.org?format=json");
        using HttpResponseMessage response = await Http.SendAsync(request, cts.Token);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        var payload = await JsonSerializer.DeserializeAsync<IpifyResp>(stream, cancellationToken: cts.Token);
        return string.IsNullOrWhiteSpace(payload?.Ip) ? null : payload.Ip;
    }

    private static async Task<long?> PingAsync(string host, TimeSpan timeout)
    {
        try
        {
            using var ping = new Ping();
            PingReply reply = await ping.SendPingAsync(host, (int)timeout.TotalMilliseconds);
            if (reply.Status == IPStatus.Success)
            {
                return reply.RoundtripTime;
            }
        }
        catch
        {
            // Ignore ping failures.
        }

        return null;
    }

    private static async Task<string?> GeoLocationAsync(string ip, TimeSpan timeout)
    {
        string? primary = await GeoFromIpApiCoAsync(ip, timeout);
        if (!string.IsNullOrWhiteSpace(primary))
        {
            return primary;
        }

        string? fallback = await GeoFromIpApiComAsync(ip, timeout);
        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return fallback;
        }

        return null;
    }

    private static async Task<string?> GeoFromIpApiCoAsync(string ip, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://ipapi.co/{ip}/json/");
        using HttpResponseMessage response = await Http.SendAsync(request, cts.Token);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        var payload = await JsonSerializer.DeserializeAsync<IpApiCoResp>(stream, cancellationToken: cts.Token);
        return FormatLocation(payload?.CountryName, payload?.City);
    }

    private static async Task<string?> GeoFromIpApiComAsync(string ip, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://ip-api.com/json/{ip}?fields=status,country,city");
        using HttpResponseMessage response = await Http.SendAsync(request, cts.Token);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        var payload = await JsonSerializer.DeserializeAsync<IpApiComResp>(stream, cancellationToken: cts.Token);
        if (!string.Equals(payload?.Status, "success", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return FormatLocation(payload?.Country, payload?.City);
    }

    private static string? FormatLocation(string? country, string? city)
    {
        if (string.IsNullOrWhiteSpace(city) && string.IsNullOrWhiteSpace(country))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(city))
        {
            return country;
        }

        if (string.IsNullOrWhiteSpace(country))
        {
            return city;
        }

        return $"{country} - {city}";
    }

    private sealed class IpifyResp
    {
        [JsonPropertyName("ip")]
        public string? Ip { get; set; }
    }

    private sealed class IpApiCoResp
    {
        [JsonPropertyName("city")]
        public string? City { get; set; }

        [JsonPropertyName("country_name")]
        public string? CountryName { get; set; }
    }

    private sealed class IpApiComResp
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("country")]
        public string? Country { get; set; }

        [JsonPropertyName("city")]
        public string? City { get; set; }
    }

    private sealed class UpdateInfo
    {
        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("notes")]
        public string? Notes { get; set; }
    }

    private void NotifyEnabled_Changed(object sender, RoutedEventArgs e)
    {
        // No-op for now.
    }

    private void SaveMailButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            MailSettings settings = ReadSettingsFromUi();
            SaveMailSettings(settings);
            MailStatusText.Text = "Paramètres enregistrés.";
            MessageBox.Show("Paramètres enregistrés.", "Notifications", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MailStatusText.Text = ex.Message;
            MessageBox.Show(ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void TestMailButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            MailSettings settings = ReadSettingsFromUi();
            await SendMailAsync(settings, "Test Netcheck", "Ceci est un email de test.");
            MailStatusText.Text = "Email de test envoyé.";
            MessageBox.Show("Email de test envoyé.", "Notifications", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MailStatusText.Text = ex.Message;
            MessageBox.Show(ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ReloadMailButton_Click(object sender, RoutedEventArgs e)
    {
        LoadMailSettings();
        MailStatusText.Text = "Paramètres rechargés.";
    }

    private void ClearMailButton_Click(object sender, RoutedEventArgs e)
    {
        SmtpHostBox.Text = "";
        SmtpPortBox.Text = "587";
        SmtpSslBox.IsChecked = true;
        SmtpUserBox.Text = "";
        SmtpPassBox.Password = "";
        MailFromBox.Text = "";
        MailToBox.Text = "";
        MailSubjectBox.Text = "IP publique changée";
        NotifyEnabled.IsChecked = false;
        MailStatusText.Text = "Paramètres effacés (non enregistrés).";
    }

    private async Task NotifyIfIpChangedAsync(string currentIp)
    {
        if (string.Equals(_lastIp, currentIp, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        bool hadPrevious = !string.IsNullOrWhiteSpace(_lastIp);
        _lastIp = currentIp;

        if (!hadPrevious)
        {
            return;
        }

        if (DateTime.Now - _lastMailSent < MailCooldown)
        {
            MailStatusText.Text = $"Email ignoré (anti-spam {MailCooldown.TotalMinutes:0} min).";
            return;
        }

        MailSettings settings;
        try
        {
            settings = ReadSettingsFromUi();
        }
        catch
        {
            return;
        }

        string subject = string.IsNullOrWhiteSpace(settings.Subject) ? "IP publique changée" : settings.Subject;
        string body = $"Nouvelle IP publique: {currentIp}\nDate: {DateTime.Now:dd/MM/yyyy HH:mm:ss}";
        try
        {
            await SendMailAsync(settings, subject, body);
            _lastMailSent = DateTime.Now;
            MailStatusText.Text = "Email envoyé.";
        }
        catch (Exception ex)
        {
            MailStatusText.Text = $"Erreur email: {ex.Message}";
        }
    }

    private static async Task SendMailAsync(MailSettings settings, string subject, string body)
    {
        using var message = new MailMessage(settings.From, settings.To, subject, body);
        using var client = new SmtpClient(settings.SmtpHost, settings.SmtpPort)
        {
            EnableSsl = settings.UseSsl,
            Credentials = new System.Net.NetworkCredential(settings.Username, settings.Password)
        };

        await client.SendMailAsync(message);
    }

    private MailSettings ReadSettingsFromUi()
    {
        if (string.IsNullOrWhiteSpace(SmtpHostBox.Text))
        {
            throw new InvalidOperationException("Le serveur SMTP est requis.");
        }

        if (!int.TryParse(SmtpPortBox.Text, out int port))
        {
            throw new InvalidOperationException("Le port SMTP est invalide.");
        }

        string user = SmtpUserBox.Text.Trim();
        string pass = SmtpPassBox.Password;
        string from = MailFromBox.Text.Trim();
        string to = MailToBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
        {
            throw new InvalidOperationException("Login et mot de passe requis.");
        }

        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
        {
            throw new InvalidOperationException("Adresses expéditeur et destinataire requises.");
        }

        return new MailSettings
        {
            SmtpHost = SmtpHostBox.Text.Trim(),
            SmtpPort = port,
            UseSsl = SmtpSslBox.IsChecked == true,
            Username = user,
            Password = pass,
            From = from,
            To = to,
            Subject = MailSubjectBox.Text.Trim()
        };
    }

    private void LoadMailSettings()
    {
        try
        {
            string path = GetSettingsPath();
            if (!File.Exists(path))
            {
                return;
            }

            byte[] encrypted = File.ReadAllBytes(path);
            byte[] data = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            var settings = JsonSerializer.Deserialize<MailSettings>(data);
            if (settings == null)
            {
                return;
            }

            SmtpHostBox.Text = settings.SmtpHost;
            SmtpPortBox.Text = settings.SmtpPort.ToString();
            SmtpSslBox.IsChecked = settings.UseSsl;
            SmtpUserBox.Text = settings.Username;
            SmtpPassBox.Password = settings.Password;
            MailFromBox.Text = settings.From;
            MailToBox.Text = settings.To;
            MailSubjectBox.Text = settings.Subject;
        }
        catch
        {
            // Ignore load errors and let the user re-enter.
        }
    }

    private void SaveMailSettings(MailSettings settings)
    {
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(settings);
        byte[] encrypted = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(GetSettingsPath(), encrypted);
    }

    private static string GetSettingsPath()
    {
        return Path.Combine(AppContext.BaseDirectory, SettingsFileName);
    }

    private static string GetWindowStatePath()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Netcheck");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, WindowStateFileName);
    }

    private void RestoreWindowPosition()
    {
        try
        {
            string path = GetWindowStatePath();
            if (!File.Exists(path))
            {
                CenterOnScreen();
                return;
            }

            WindowStateSnapshot? snapshot = JsonSerializer.Deserialize<WindowStateSnapshot>(File.ReadAllBytes(path));
            if (snapshot == null)
            {
                CenterOnScreen();
                return;
            }

            WindowState = snapshot.State;

            if (snapshot.State == WindowState.Normal)
            {
                Left = snapshot.Left;
                Top = snapshot.Top;
            }
            else
            {
                Left = snapshot.RestoreLeft;
                Top = snapshot.RestoreTop;
            }

            EnsureOnScreen();
        }
        catch
        {
            CenterOnScreen();
        }
    }

    private void SaveWindowPosition()
    {
        try
        {
            Rect bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
            var snapshot = new WindowStateSnapshot
            {
                Left = Left,
                Top = Top,
                RestoreLeft = bounds.Left,
                RestoreTop = bounds.Top,
                State = WindowState
            };
            File.WriteAllBytes(GetWindowStatePath(), JsonSerializer.SerializeToUtf8Bytes(snapshot));
        }
        catch
        {
            // Ignore save failures.
        }
    }

    private void CenterOnScreen()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - ActualWidth) / 2;
        Top = workArea.Top + (workArea.Height - ActualHeight) / 2;
    }

    private void EnsureOnScreen()
    {
        var workArea = SystemParameters.WorkArea;
        if (Left < workArea.Left) Left = workArea.Left;
        if (Top < workArea.Top) Top = workArea.Top;
        if (Left > workArea.Right - 40) Left = workArea.Right - 40;
        if (Top > workArea.Bottom - 40) Top = workArea.Bottom - 40;
    }

    private sealed class MailSettings
    {
        public string SmtpHost { get; set; } = "";
        public int SmtpPort { get; set; }
        public bool UseSsl { get; set; }
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string From { get; set; } = "";
        public string To { get; set; } = "";
        public string Subject { get; set; } = "";
    }

    private sealed class WindowStateSnapshot
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double RestoreLeft { get; set; }
        public double RestoreTop { get; set; }
        public WindowState State { get; set; }
    }
}

using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Authentication;

namespace PortalMonitor;

public sealed class AvailabilityChecker
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = true })
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public async Task<CheckResult> CheckAsync(MonitorTarget target, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (Uri.TryCreate(target.Address, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                return await CheckHttpAsync(uri, cancellationToken, stopwatch);
            }

            return target.Port is int port
                ? await CheckTcpAsync(target.Address.Trim(), port, cancellationToken, stopwatch)
                : await CheckPingAsync(target.Address.Trim(), cancellationToken, stopwatch);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, "NIEDOSTĘPNY", "Przekroczono limit 20 sekund oczekiwania na odpowiedź.", stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            return new(false, "PRZERWANO", "Sprawdzanie zostało przerwane.", stopwatch.Elapsed);
        }
        catch (AuthenticationException)
        {
            return new(false, "BŁĄD TLS", "Nie udało się bezpiecznie potwierdzić certyfikatu SSL/TLS.", stopwatch.Elapsed);
        }
        catch (HttpRequestException ex) when (ex.InnerException is AuthenticationException)
        {
            return new(false, "BŁĄD TLS", "Portal odrzucił połączenie SSL/TLS albo jego certyfikat jest nieprawidłowy.", stopwatch.Elapsed);
        }
        catch (HttpRequestException ex) when (ex.InnerException is SocketException socket)
        {
            return new(false, "NIEDOSTĘPNY", DescribeSocketError(socket), stopwatch.Elapsed);
        }
        catch (SocketException ex)
        {
            return new(false, "NIEDOSTĘPNY", DescribeSocketError(ex), stopwatch.Elapsed);
        }
        catch (HttpRequestException ex)
        {
            return new(false, "NIEDOSTĘPNY", $"Błąd połączenia HTTP: {ex.Message}", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            return new(false, "BŁĄD", $"Nieoczekiwany błąd: {ex.Message}", stopwatch.Elapsed);
        }
    }

    private static async Task<CheckResult> CheckHttpAsync(Uri uri, CancellationToken token, Stopwatch stopwatch)
    {
        using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
        var code = (int)response.StatusCode;
        if (response.IsSuccessStatusCode)
            return new(true, "DOSTĘPNY", $"HTTP {code} {response.ReasonPhrase}", stopwatch.Elapsed);

        var reason = code switch
        {
            401 or 403 => "Dostęp wymaga uprawnień lub serwer blokuje żądanie.",
            404 => "Serwer działa, ale wskazana strona nie istnieje (HTTP 404).",
            429 => "Serwer ogranicza liczbę zapytań (HTTP 429).",
            >= 500 and <= 599 => "Błąd po stronie serwera lub jego bramki/proxy.",
            _ => $"Serwer zwrócił HTTP {code} {response.ReasonPhrase}."
        };
        return new(false, "BŁĄD HTTP", reason, stopwatch.Elapsed);
    }

    private static async Task<CheckResult> CheckTcpAsync(string host, int port, CancellationToken token, Stopwatch stopwatch)
    {
        if (string.IsNullOrWhiteSpace(host))
            return new(false, "BŁĘDNA KONFIGURACJA", "Nie podano adresu IP ani nazwy hosta.", stopwatch.Elapsed);
        if (port is < 1 or > 65535)
            return new(false, "BŁĘDNA KONFIGURACJA", "Port musi mieć wartość od 1 do 65535.", stopwatch.Elapsed);

        // The explicit lookup lets us distinguish a DNS issue from a closed TCP port.
        var addresses = await Dns.GetHostAddressesAsync(host, token);
        if (addresses.Length == 0)
            return new(false, "BŁĄD DNS", "Nie znaleziono adresu IP dla podanej nazwy hosta.", stopwatch.Elapsed);

        using var client = new TcpClient();
        await client.ConnectAsync(host, port, token);
        return new(true, "DOSTĘPNY", $"Port TCP {port} odpowiada.", stopwatch.Elapsed);
    }

    private static async Task<CheckResult> CheckPingAsync(string host, CancellationToken token, Stopwatch stopwatch)
    {
        if (string.IsNullOrWhiteSpace(host))
            return new(false, "BŁĘDNA KONFIGURACJA", "Nie podano adresu IP ani nazwy hosta.", stopwatch.Elapsed);

        var addresses = await Dns.GetHostAddressesAsync(host, token);
        if (addresses.Length == 0)
            return new(false, "BŁĄD DNS", "Nie znaleziono adresu IP dla podanej nazwy hosta.", stopwatch.Elapsed);

        using var ping = new Ping();
        var reply = await ping.SendPingAsync(host, 10_000).WaitAsync(token);
        if (reply.Status == IPStatus.Success)
            return new(true, "DOSTĘPNY", $"Host odpowiada na ping ({reply.RoundtripTime} ms).", stopwatch.Elapsed);

        var reason = reply.Status switch
        {
            IPStatus.TimedOut => "Host nie odpowiedział na ping przed upływem 10 sekund.",
            IPStatus.DestinationHostUnreachable => "Host docelowy jest nieosiągalny.",
            IPStatus.DestinationNetworkUnreachable => "Sieć docelowa jest nieosiągalna.",
            _ => $"Ping zakończył się statusem: {reply.Status}."
        };
        return new(false, "NIEDOSTĘPNY", reason, stopwatch.Elapsed);
    }

    private static string DescribeSocketError(SocketException error) => error.SocketErrorCode switch
    {
        SocketError.HostNotFound or SocketError.NoData => "Nie można odnaleźć hosta w DNS.",
        SocketError.ConnectionRefused => "Host odpowiada, lecz port odrzuca połączenie.",
        SocketError.TimedOut => "Host lub sieć nie odpowiedziały przed upływem limitu czasu.",
        SocketError.NetworkUnreachable => "Sieć docelowa jest nieosiągalna.",
        SocketError.HostUnreachable => "Host jest nieosiągalny.",
        _ => $"Błąd sieci ({error.SocketErrorCode}): {error.Message}"
    };
}

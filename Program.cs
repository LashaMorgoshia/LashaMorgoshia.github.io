using System.Net;
using System.Text.Json;

const string DefaultTeslaApiBase = "https://fleet-api.prd.eu.vn.cloud.tesla.com";

using var httpClient = new HttpClient
{
    Timeout = TimeSpan.FromSeconds(30)
};

var exitCode = await RunAsync(args, httpClient);
Environment.Exit(exitCode);

static async Task<int> RunAsync(string[] args, HttpClient httpClient)
{
    if (args.Length == 0 || args.Any(arg => arg is "-h" or "--help"))
    {
        PrintUsage();
        return args.Length == 0 ? 1 : 0;
    }

    var vin = args[0].Trim();
    if (string.IsNullOrWhiteSpace(vin))
    {
        Console.Error.WriteLine("Missing VIN.");
        PrintUsage();
        return 1;
    }

    var options = args.Skip(1).ToArray();
    var live = false;
    var raw = false;

    foreach (var option in options)
    {
        switch (option)
        {
            case "--live":
                live = true;
                break;
            case "--raw":
                raw = true;
                break;
            default:
                Console.Error.WriteLine($"Unknown option: {option}");
                PrintUsage();
                return 1;
        }
    }

    var accessToken = Environment.GetEnvironmentVariable("TESLA_ACCESS_TOKEN");
    if (string.IsNullOrWhiteSpace(accessToken))
    {
        Console.Error.WriteLine("Missing TESLA_ACCESS_TOKEN environment variable.");
        Console.Error.WriteLine("Set TESLA_ACCESS_TOKEN to a valid Tesla Fleet API OAuth access token and try again.");
        return 1;
    }

    var baseUrl = Environment.GetEnvironmentVariable("TESLA_API_BASE");
    if (string.IsNullOrWhiteSpace(baseUrl))
    {
        baseUrl = DefaultTeslaApiBase;
    }

    if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
    {
        Console.Error.WriteLine($"TESLA_API_BASE is not a valid absolute URL: {baseUrl}");
        return 1;
    }

    var path = live
        ? $"/api/1/vehicles/{Uri.EscapeDataString(vin)}/vehicle_data"
        : $"/api/1/vehicles/{Uri.EscapeDataString(vin)}";

    TeslaApiResult result;
    try
    {
        result = await CallTeslaApiAsync(httpClient, baseUri, path, accessToken);
    }
    catch (TimeoutException ex)
    {
        Console.Error.WriteLine($"Tesla API request timed out: {ex.Message}");
        return 1;
    }
    catch (HttpRequestException ex)
    {
        Console.Error.WriteLine($"Tesla API request failed: {ex.Message}");
        return 1;
    }

    if (!result.IsSuccessStatusCode)
    {
        PrintHttpError(result);
        return 1;
    }

    try
    {
        using var document = JsonDocument.Parse(result.Body);
        var root = document.RootElement;

        if (live && IsVehicleUnavailable(root))
        {
            Console.Error.WriteLine("Vehicle appears to be offline or asleep. Try again later, or wake the vehicle before requesting live data.");
            return 1;
        }

        if (raw && !live)
        {
            Console.WriteLine(PrettyJson(root));
            return 0;
        }

        var response = GetResponseElement(root);
        if (response.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            Console.Error.WriteLine("Tesla API response did not contain a usable 'response' object.");
            return 1;
        }

        if (live)
        {
            PrintLiveVehicleData(response);
        }
        else
        {
            PrintBasicVehicleInfo(response);
        }

        if (raw)
        {
            Console.WriteLine();
            Console.WriteLine("Raw JSON:");
            Console.WriteLine(PrettyJson(root));
        }

        return 0;
    }
    catch (JsonException ex)
    {
        Console.Error.WriteLine($"Tesla API returned invalid JSON: {ex.Message}");
        return 1;
    }
}

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run -- <VIN>");
    Console.WriteLine("  dotnet run -- <VIN> --live");
    Console.WriteLine("  dotnet run -- <VIN> --raw");
    Console.WriteLine("  dotnet run -- <VIN> --live --raw");
    Console.WriteLine();
    Console.WriteLine("Environment variables:");
    Console.WriteLine("  TESLA_ACCESS_TOKEN   Required Tesla Fleet API OAuth access token");
    Console.WriteLine($"  TESLA_API_BASE       Optional API base URL, defaults to {DefaultTeslaApiBase}");
}

static void PrintBasicVehicleInfo(JsonElement vehicle)
{
    Console.WriteLine("Vehicle Info:");
    PrintIfExists(vehicle, "VIN", "vin");
    PrintIfExists(vehicle, "id_s", "id_s");
    PrintIfExists(vehicle, "vehicle_id", "vehicle_id");
    PrintIfExists(vehicle, "display_name", "display_name");
    PrintIfExists(vehicle, "state", "state");
    PrintIfExists(vehicle, "option_codes", "option_codes");
    PrintIfExists(vehicle, "color", "color");
}

static void PrintLiveVehicleData(JsonElement vehicleData)
{
    Console.WriteLine("Vehicle Live Data:");

    if (vehicleData.TryGetProperty("vehicle_state", out var vehicleState))
    {
        Console.WriteLine();
        Console.WriteLine("vehicle_state:");
        PrintIfExists(vehicleState, "car_version", "car_version");
        PrintIfExists(vehicleState, "odometer", "odometer");
        PrintIfExists(vehicleState, "locked", "locked");
        PrintIfExists(vehicleState, "sentry_mode", "sentry_mode");
    }
    else
    {
        Console.WriteLine();
        Console.WriteLine("vehicle_state: not available");
    }

    if (vehicleData.TryGetProperty("charge_state", out var chargeState))
    {
        Console.WriteLine();
        Console.WriteLine("charge_state:");
        PrintIfExists(chargeState, "battery_level", "battery_level");
        PrintIfExists(chargeState, "charging_state", "charging_state");
        PrintIfExists(chargeState, "charge_limit_soc", "charge_limit_soc");
        PrintIfExists(chargeState, "battery_range", "battery_range");
        PrintIfExists(chargeState, "est_battery_range", "est_battery_range");
        PrintIfExists(chargeState, "rated_battery_range", "rated_battery_range");
        PrintIfExists(chargeState, "charger_power", "charger_power");
        PrintIfExists(chargeState, "charger_voltage", "charger_voltage");
        PrintIfExists(chargeState, "charger_actual_current", "charger_actual_current");
    }
    else
    {
        Console.WriteLine();
        Console.WriteLine("charge_state: not available");
    }
}

static void PrintIfExists(JsonElement parent, string label, string propertyName)
{
    if (!parent.TryGetProperty(propertyName, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
    {
        return;
    }

    Console.WriteLine($"{label}: {JsonElementToString(value)}");
}

static string PrettyJson(JsonElement element)
{
    return JsonSerializer.Serialize(element, new JsonSerializerOptions
    {
        WriteIndented = true
    });
}

static async Task<TeslaApiResult> CallTeslaApiAsync(
    HttpClient httpClient,
    Uri baseUri,
    string path,
    string accessToken,
    CancellationToken cancellationToken = default)
{
    var requestUri = new Uri(baseUri, path);
    using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
    request.Headers.Accept.ParseAdd("application/json");

    try
    {
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return new TeslaApiResult(response.StatusCode, body, response.ReasonPhrase);
    }
    catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
    {
        throw new TimeoutException("No response was received within 30 seconds.", ex);
    }
}

static void PrintHttpError(TeslaApiResult result)
{
    var detail = GetErrorDetail(result.Body);

    switch (result.StatusCode)
    {
        case HttpStatusCode.Unauthorized:
            Console.Error.WriteLine("Tesla API returned 401 Unauthorized. Check that TESLA_ACCESS_TOKEN is valid and not expired.");
            break;
        case HttpStatusCode.Forbidden:
            Console.Error.WriteLine("Tesla API returned 403 Forbidden. The token may not have access to this vehicle or required scopes.");
            break;
        case HttpStatusCode.NotFound:
            Console.Error.WriteLine("Tesla API returned 404 Not Found. Check the VIN and whether the vehicle is visible to this account.");
            break;
        case HttpStatusCode.RequestTimeout:
        case HttpStatusCode.Conflict:
        case HttpStatusCode.PreconditionFailed:
        case HttpStatusCode.ServiceUnavailable:
        case HttpStatusCode.GatewayTimeout:
            Console.Error.WriteLine($"Tesla API returned {(int)result.StatusCode} {result.StatusCode}. The vehicle may be offline, asleep, or temporarily unavailable.");
            break;
        default:
            Console.Error.WriteLine($"Tesla API returned {(int)result.StatusCode} {result.StatusCode}.");
            break;
    }

    if (!string.IsNullOrWhiteSpace(detail))
    {
        Console.Error.WriteLine($"Details: {detail}");
    }
    else if (!string.IsNullOrWhiteSpace(result.ReasonPhrase))
    {
        Console.Error.WriteLine($"Details: {result.ReasonPhrase}");
    }
}

static string? GetErrorDetail(string body)
{
    if (string.IsNullOrWhiteSpace(body))
    {
        return null;
    }

    try
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        foreach (var propertyName in new[] { "error", "error_description", "message", "reason" })
        {
            if (root.TryGetProperty(propertyName, out var value))
            {
                var text = JsonElementToString(value);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        if (root.TryGetProperty("response", out var response))
        {
            foreach (var propertyName in new[] { "error", "message", "reason" })
            {
                if (response.TryGetProperty(propertyName, out var value))
                {
                    var text = JsonElementToString(value);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }
        }
    }
    catch (JsonException)
    {
        return body.Length <= 500 ? body : $"{body[..500]}...";
    }

    return null;
}

static bool IsVehicleUnavailable(JsonElement root)
{
    var text = root.ToString();
    return text.Contains("offline", StringComparison.OrdinalIgnoreCase)
        || text.Contains("asleep", StringComparison.OrdinalIgnoreCase)
        || text.Contains("vehicle unavailable", StringComparison.OrdinalIgnoreCase)
        || text.Contains("vehicle is unavailable", StringComparison.OrdinalIgnoreCase);
}

static JsonElement GetResponseElement(JsonElement root)
{
    return root.TryGetProperty("response", out var response) ? response : root;
}

static string JsonElementToString(JsonElement element)
{
    return element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => string.Empty,
        JsonValueKind.Undefined => string.Empty,
        _ => element.GetRawText()
    };
}

internal sealed record TeslaApiResult(HttpStatusCode StatusCode, string Body, string? ReasonPhrase)
{
    public bool IsSuccessStatusCode => (int)StatusCode is >= 200 and <= 299;
}

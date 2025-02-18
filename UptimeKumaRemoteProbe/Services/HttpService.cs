﻿namespace UptimeKumaRemoteProbe.Services;

public class HttpService(ILogger<HttpService> logger, HttpClient httpClient, IHttpClientFactory httpClientFactory, PushService pushService)
{
    public async Task CheckHttpAsync(Endpoint endpoint)
    {
        httpClient = httpClientFactory.CreateClient(endpoint.IgnoreSSL ? "IgnoreSSL" : "Default");

        var stopwatch = Stopwatch.StartNew();

        string content;

        HttpResponseMessage result;

        try
        {
            result = await httpClient.GetAsync(endpoint.Destination);
            content = await result.Content.ReadAsStringAsync();

            logger.LogInformation("Http: {endpoint.Destination} {result.StatusCode}",
                endpoint.Destination, result.StatusCode);

            if (endpoint.Keyword != "" && !content.Contains(endpoint.Keyword)) throw new ArgumentNullException(nameof(endpoint), "Keyword not found.");
        }
        catch
        {
            logger.LogError("Error trying get {endpoint.Destination}", endpoint.Destination);
            return;
        }

        if (result is not null && (result.IsSuccessStatusCode || ((int)result.StatusCode) == endpoint.SuccessStatusCode))
        {
            await pushService.PushUpAsync(endpoint.PushUpUri, stopwatch.ElapsedMilliseconds);
        }
        else if (result is not null)
        {
            await pushService.PushDownAsync(endpoint.PushDownUri, stopwatch.ElapsedMilliseconds, result.StatusCode.ToString());
        }
    }
}

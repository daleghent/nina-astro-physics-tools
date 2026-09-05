#region "copyright"

/*
    Copyright (c) 2024 Dale Ghent <daleg@elemental.org>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/
*/

#endregion "copyright"

using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Core.Utility;
using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DaleGhent.NINA.AstroPhysicsTools.AppmApi {

    public class AppmApi {

        // A single HttpClient is shared by all instances. Creating one per request exhausts
        // ephemeral ports because disposed sockets linger in TIME_WAIT, and the status endpoint
        // is polled once per second for the entire duration of a mapping run.
        private static readonly HttpClient httpClient = new() {
            Timeout = TimeSpan.FromSeconds(30),
        };

        private readonly string host;
        private readonly int port;
        private readonly JsonSerializerSettings serializerSettings;

        public AppmApi(string host = "127.0.0.1", int port = 60011) {
            this.host = host;
            this.port = port;

            serializerSettings = new JsonSerializerSettings() {
                MissingMemberHandling = MissingMemberHandling.Ignore,
                Formatting = Formatting.Indented,
            };
        }

        public Task Start(CancellationToken ct) {
            return SendAsync("/api/MappingRun/Start", "{\"Action\":\"Start\"}", HttpMethod.Post, ct);
        }

        public Task Stop(CancellationToken ct) {
            return SendAsync("/api/MappingRun/Stop", "{\"Action\":\"Stop\"}", HttpMethod.Post, ct);
        }

        public Task Close(CancellationToken ct) {
            return SendAsync("/api/Application/Close", "{}", HttpMethod.Post, ct);
        }

        public Task<AppmMappingRunStatusResult> Status(CancellationToken ct) {
            return RequestJsonAsync<AppmMappingRunStatusResult>("/api/MappingRun/Status", null, HttpMethod.Get, ct);
        }

        public Task<AppmPointCountResult> PointCount(CancellationToken ct) {
            return RequestJsonAsync<AppmPointCountResult>("/api/MappingPoints/PointCount", null, HttpMethod.Get, ct);
        }

        public Task<AppmMappingPointsResult> MappingPoints(CancellationToken ct) {
            return RequestJsonAsync<AppmMappingPointsResult>("/api/MappingPoints", null, HttpMethod.Get, ct);
        }

        public Task<AppmMeasurementConfigurationResult> GetConfiguration(CancellationToken ct) {
            return RequestJsonAsync<AppmMeasurementConfigurationResult>("/api/MappingPoints/Configuration", null, HttpMethod.Get, ct);
        }

        public Task<AppmMeasurementConfigurationResult> SetConfiguration(AppmMeasurementConfigurationRequest config, CancellationToken ct) {
            string configSer = JsonConvert.SerializeObject(config, serializerSettings);
            return RequestJsonAsync<AppmMeasurementConfigurationResult>("/api/MappingPoints/Configuration", configSer, HttpMethod.Put, ct);
        }

        public async Task<AppmMappingRunStatusResult> WaitForApiInit(CancellationToken ct) {
            while (true) {
                ct.ThrowIfCancellationRequested();

                try {
                    var status = await Status(ct);
                    Logger.Debug("APPM is up");
                    return status;
                } catch (HttpRequestException) {
                    Logger.Debug($"APPM not yet answering on API; trying again...");
                    await Task.Delay(TimeSpan.FromSeconds(1), ct);
                }
            }
        }

        private async Task<T> RequestJsonAsync<T>(string url, string body, HttpMethod method, CancellationToken ct) {
            var content = await SendAsync(url, body, method, ct);
            return JsonConvert.DeserializeObject<T>(content, serializerSettings);
        }

        private async Task<string> SendAsync(string url, string body, HttpMethod method, CancellationToken ct) {
            var uri = new Uri($"http://{this.host}:{this.port}{url}");

            if (!uri.IsWellFormedOriginalString()) {
                throw new SequenceEntityFailedException($"Invalid or malformed URL: {uri}");
            }

            using var request = new HttpRequestMessage(method, uri);

            if (!string.IsNullOrEmpty(body)) {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            Logger.Trace($"Request URL: {request.Method} {request.RequestUri}");

            if (!string.IsNullOrEmpty(body)) {
                Logger.Trace($"Request body:{Environment.NewLine}{body}");
            }

            using var response = await httpClient.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            Logger.Trace($"Response status code: {response.StatusCode}");
            Logger.Trace($"Response body:{Environment.NewLine}{responseBody}");

            response.EnsureSuccessStatusCode();

            return responseBody;
        }
    }
}
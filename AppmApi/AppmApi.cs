#region "copyright"

/*
    Copyright Dale Ghent <daleg@elemental.org>

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

namespace DaleGhent.NINA.AstroPhysics.AppmApi {

    public class AppmApi {
        private string host;
        private int port;
        private JsonSerializerSettings serializerSettings;

        public AppmApi(string host = "127.0.0.1", int port = 60011) {
            this.host = host;
            this.port = port;

            serializerSettings = new JsonSerializerSettings() {
                CheckAdditionalContent = true,
                MissingMemberHandling = MissingMemberHandling.Error,
                Formatting = Formatting.Indented,
            };
        }

        public async Task Start(CancellationToken ct) {
            _ = await HttpRequestAsync("/api/MappingRun/Start", "{\"Action\":\"Start\"}", HttpMethod.Post, ct);
        }

        public async Task Stop(CancellationToken ct) {
            _ = await HttpRequestAsync("/api/MappingRun/Stop", "{\"Action\":\"Stop\"}", HttpMethod.Post, ct);
        }

        public async Task Close(CancellationToken ct) {
            _ = await HttpRequestAsync("/api/Application/Close", "{}", HttpMethod.Post, ct);
        }

        public async Task<AppmMappingRunStatusResult> Status(CancellationToken ct) {
            AppmMappingRunStatusResult response = null;
            var result = await HttpRequestAsync("/api/MappingRun/Status", null, HttpMethod.Get, ct);

            if (result != null) {
                response = JsonConvert.DeserializeObject<AppmMappingRunStatusResult>(result.Content.ReadAsStringAsync().Result, serializerSettings);
            }

            result.Dispose();
            return response;
        }

        public async Task<AppmPointCountResult> PointCount(CancellationToken ct) {
            AppmPointCountResult response = null;
            var result = await HttpRequestAsync("/api/MappingPoints/PointCount", null, HttpMethod.Get, ct);

            if (result != null) {
                response = JsonConvert.DeserializeObject<AppmPointCountResult>(result.Content.ReadAsStringAsync().Result, serializerSettings);
            }

            result.Dispose();
            return response;
        }

        public async Task<AppmMappingPointsResult> MappingPoints(CancellationToken ct) {
            AppmMappingPointsResult response = null;
            var result = await HttpRequestAsync("/api/MappingPoints", null, HttpMethod.Get, ct);

            if (result != null) {
                response = JsonConvert.DeserializeObject<AppmMappingPointsResult>(result.Content.ReadAsStringAsync().Result, serializerSettings);
            }

            result.Dispose();
            return response;
        }

        public async Task<AppmMeasurementConfigurationResult> GetConfiguration(CancellationToken ct) {
            AppmMeasurementConfigurationResult response = null;
            var result = await HttpRequestAsync("/api/MappingPoints/Configuration", null, HttpMethod.Get, ct);

            if (result != null) {
                response = JsonConvert.DeserializeObject<AppmMeasurementConfigurationResult>(result.Content.ReadAsStringAsync().Result, serializerSettings);
            }

            result.Dispose();
            return response;
        }

        public async Task<AppmMeasurementConfigurationResult> SetConfiguration(AppmMeasurementConfigurationRequest config, CancellationToken ct) {
            AppmMeasurementConfigurationResult response = null;

            string configSer = JsonConvert.SerializeObject(config, serializerSettings);
            var result = await HttpRequestAsync("/api/MappingPoints/Configuration", configSer, HttpMethod.Put, ct);

            if (result != null) {
                response = JsonConvert.DeserializeObject<AppmMeasurementConfigurationResult>(result.Content.ReadAsStringAsync().Result, serializerSettings);
            }

            result.Dispose();
            return response;
        }

        public async Task<AppmMappingRunStatusResult> WaitForApiInit(CancellationToken ct) {
            var appm = new AppmApi();
            AppmMappingRunStatusResult status = null;

            while (!ct.IsCancellationRequested) {
                try {
                    status = await appm.Status(ct);
                    break;
                } catch (HttpRequestException) {
                    Logger.Debug($"APPM not yet answering on API; trying again...");
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }
            }

            Logger.Debug("APPM is up");
            return status;
        }

        private async Task<HttpResponseMessage> HttpRequestAsync(string url, string body, HttpMethod method, CancellationToken ct) {
            var uri = new Uri($"http://{this.host}:{this.port}{url}");

            if (!uri.IsWellFormedOriginalString()) {
                throw new SequenceEntityFailedException($"Invalid or malformed URL: {uri}");
            }

            var request = new HttpRequestMessage(method, uri);

            if (!string.IsNullOrEmpty(body)) {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            Logger.Debug($"Request URL: {request.Method} {request.RequestUri}");
            if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head) {
                Logger.Trace($"Request body:{Environment.NewLine}{request.Content?.ReadAsStringAsync().Result}");
            }

            var client = new HttpClient();
            var response = await client.SendAsync(request, ct);
            client.Dispose();

            Logger.Debug($"Response status code: {response.StatusCode}");
            Logger.Trace($"Response body:{Environment.NewLine}{response.Content?.ReadAsStringAsync().Result}");

            return response;
        }
    }
}
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
            _ = await HttpRequestAsync("/api/MappingRun/Start", "", ct);
        }

        public async Task Stop(CancellationToken ct) {
            _ = await HttpRequestAsync("/api/MappingRun/Stop", "", ct);
        }

        public async Task<AppmMappingRunStatusResult> Status(CancellationToken ct) {
            AppmMappingRunStatusResult response = null;
            var result = await HttpRequestAsync("/api/MappingRun/Status", null, ct);

            if (result != null) {
                response = JsonConvert.DeserializeObject<AppmMappingRunStatusResult>(result.Content.ReadAsStringAsync().Result, serializerSettings);
            }

            return response;
        }

        public async Task<AppmPointCountResult> PointCount(CancellationToken ct) {
            AppmPointCountResult response = null;
            var result = await HttpRequestAsync("/api/MappingPoints/PointCount", null, ct);

            if (result != null) {
                response = JsonConvert.DeserializeObject<AppmPointCountResult>(result.Content.ReadAsStringAsync().Result, serializerSettings);
            }

            return response;
        }

        public async Task<AppmMappingPointsResult> MappingPoints(CancellationToken ct) {
            AppmMappingPointsResult response = null;
            var result = await HttpRequestAsync("/api/MappingPoints", null, ct);

            if (result != null) {
                response = JsonConvert.DeserializeObject<AppmMappingPointsResult>(result.Content.ReadAsStringAsync().Result, serializerSettings);
            }

            return response;
        }

        public async Task<AppmMeasurementConfigurationResult> GetConfiguration(CancellationToken ct) {
            AppmMeasurementConfigurationResult response = null;
            var result = await HttpRequestAsync("/api/MappingPoints/Configuration", null, ct);

            if (result != null) {
                response = JsonConvert.DeserializeObject<AppmMeasurementConfigurationResult>(result.Content.ReadAsStringAsync().Result, serializerSettings);
            }

            return response;
        }

        public async Task<AppmMeasurementConfigurationResult> SetConfiguration(AppmMeasurementConfigurationRequest config, CancellationToken ct) {
            AppmMeasurementConfigurationResult response = null;

            string configSer = JsonConvert.SerializeObject(config, serializerSettings);
            var result = await HttpRequestAsync("/api/MappingPoints/Configuration", configSer, ct);

            if (result != null) {
                response = JsonConvert.DeserializeObject<AppmMeasurementConfigurationResult>(result.Content.ReadAsStringAsync().Result, serializerSettings);
            }

            return response;
        }

        public async Task<bool> WaitForApiInit(CancellationToken ct) {
            bool success = false;

            while (!ct.IsCancellationRequested) {
                try {
                    await HttpRequestAsync("/", "", ct);
                    success = true;
                    break;
                } catch (HttpRequestException) {
                    Logger.Trace($"APPM not yet answering on API; trying again...");
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }
            }

            return success;
        }

        public async Task<AppmMappingRunStatusResult> WaitForMappingState(string status, CancellationToken ct) {
            var appmStatus = await Status(ct); ;

            while (!appmStatus.Status.MappingRunState.Equals(status) && !ct.IsCancellationRequested) {
                await Task.Delay(TimeSpan.FromSeconds(1));
                appmStatus = await Status(ct);
            }

            return appmStatus;
        }

        private async Task<HttpResponseMessage> HttpRequestAsync(string url, string body, CancellationToken ct) {
            var uri = new Uri($"http://{this.host}:{this.port}{url}");

            if (!uri.IsWellFormedOriginalString()) {
                throw new SequenceEntityFailedException($"Invalid or malformed URL: {uri}");
            }

            var request = new HttpRequestMessage(HttpMethod.Get, uri);

            if (body != null) {
                request.Method = HttpMethod.Put;
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            Logger.Debug($"Request URL: {request.RequestUri}");
            Logger.Debug($"Request type: {request.Method}");
            Logger.Debug($"Request body:{Environment.NewLine}{request.Content?.ReadAsStringAsync().Result}");

            var client = new HttpClient();
            var response = await client.SendAsync(request, ct);
            client.Dispose();

            Logger.Debug($"Response status code: {response.StatusCode}");
            Logger.Debug($"Response body:{Environment.NewLine}{response.Content?.ReadAsStringAsync().Result}");

            return response;
        }
    }
}
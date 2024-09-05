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

namespace DaleGhent.NINA.AstroPhysicsTools.ApccApi {

    public class ApccApi {
        private readonly string host;
        private readonly int port;
        private readonly JsonSerializerSettings serializerSettings;

        public ApccApi(string host = "127.0.0.1", int port = 60001) {
            this.host = host;
            this.port = port;

            serializerSettings = new JsonSerializerSettings() {
                CheckAdditionalContent = true,
                MissingMemberHandling = MissingMemberHandling.Error,
                Formatting = Formatting.Indented,
            };
        }

        public async Task<ApccSendCommandResponse> SendCommand(string command, CancellationToken ct) {
            ApccSendCommandResponse response = null;

            var sendCommand = new ApccSendCommand() {
                RegValue = "0",
                Command = command,
            };

            var result = await HttpRequestAsync("/api/mount/sendcmd", JsonConvert.SerializeObject(sendCommand, serializerSettings), HttpMethod.Post, ct);

            if (result != null) {
                response = JsonConvert.DeserializeObject<ApccSendCommandResponse>(result.Content.ReadAsStringAsync(ct).Result, serializerSettings);
            }

            result.Dispose();
            return response;
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
                Logger.Trace($"Request body:{Environment.NewLine}{request.Content?.ReadAsStringAsync(ct).Result}");
            }

            var client = new HttpClient();
            var response = await client.SendAsync(request, ct);
            client.Dispose();

            Logger.Debug($"Response status code: {response.StatusCode}");
            Logger.Trace($"Response body:{Environment.NewLine}{response.Content?.ReadAsStringAsync(ct).Result}");

            return response;
        }
    }
}
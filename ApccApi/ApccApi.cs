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

        // A single client is shared by every request. Creating and disposing one per request leaks
        // sockets into TIME_WAIT, and the flip evaluation issues a request at every sequence item
        // boundary for the entire night.
        //
        // The timeout is well below the 100 second default because these calls are made synchronously
        // from the UI thread, where the default would present as a multi-minute freeze if APCC accepts
        // a connection but never answers. APCC is normally on the same machine, so a slow reply means
        // something is wrong and failing quickly is better than blocking.
        private static readonly HttpClient client = new() {
            Timeout = TimeSpan.FromSeconds(10),
        };

        private readonly string host;
        private readonly int port;
        private readonly JsonSerializerSettings serializerSettings;

        public ApccApi(string host = "127.0.0.1", int port = 60001) {
            this.host = host;
            this.port = port;

            serializerSettings = new JsonSerializerSettings() {
                MissingMemberHandling = MissingMemberHandling.Ignore,
                Formatting = Formatting.Indented,
            };
        }

        public async Task<ApccSendCommandResponse> SendCommand(string command, CancellationToken ct) {
            var sendCommand = new ApccSendCommand() {
                RegValue = "0",
                Command = command,
            };

            var result = await HttpRequestAsync("/api/mount/sendcmd", JsonConvert.SerializeObject(sendCommand, serializerSettings), HttpMethod.Post, ct).ConfigureAwait(false);

            return result == null ? null : JsonConvert.DeserializeObject<ApccSendCommandResponse>(result, serializerSettings);
        }

        public async Task<ApccMeridianLimits> GetMeridianLimits(CancellationToken ct) {
            var result = await HttpRequestAsync("/api/mount/meridianlimits", null, HttpMethod.Get, ct).ConfigureAwait(false);

            return result == null ? null : JsonConvert.DeserializeObject<ApccMeridianLimits>(result, serializerSettings);
        }

        private async Task<string> HttpRequestAsync(string url, string body, HttpMethod method, CancellationToken ct) {
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

            // ConfigureAwait(false) throughout: these methods are ultimately consumed by synchronous
            // callers via the meridian limit cache, which blocks on the request when it has nothing
            // cached to serve. Capturing a context here would be pointless work at best, and would
            // reintroduce a deadlock if any caller ever blocked on the UI thread again.
            using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            Logger.Trace($"Response status code: {response.StatusCode}");
            Logger.Trace($"Response body:{Environment.NewLine}{content}");

            return content;
        }
    }
}
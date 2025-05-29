#region "copyright"

/*
    Copyright (c) 2024 Dale Ghent <daleg@elemental.org>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/
*/

#endregion "copyright"

using Newtonsoft.Json;
using System.Collections.Generic;

namespace DaleGhent.NINA.AstroPhysicsTools.ApccApi {

    public class ApccSendCommand {

        [JsonProperty]
        public string RegValue { get; set; }

        [JsonProperty]
        public string Command { get; set; }
    }

    public class ApccSendCommandResponse {

        [JsonProperty]
        public ApccResponseStatus ResponseStatus { get; set; }

        [JsonProperty]
        public bool Success { get; set; }

        [JsonProperty]
        public string Result { get; set; }

        [JsonProperty]
        public string CommandString { get; set; }

        [JsonProperty]
        public string ResponseString { get; set; }
    }

    public class ApccResponseStatus {

        [JsonProperty]
        public string ErrorCode { get; set; }

        [JsonProperty]
        public string Message { get; set; }

        [JsonProperty]
        public string StackTrace { get; set; }

        [JsonProperty]
        public List<ApccResponseError> Errors { get; set; }

        [JsonProperty]
        public ApccResponseMeta Meta { get; set; }
    }

    public class ApccResponseError {

        [JsonProperty]
        public string ErrorCode { get; set; }

        [JsonProperty]
        public string FieldName { get; set; }

        [JsonProperty]
        public string Message { get; set; }

        [JsonProperty]
        public ApccResponseMeta Meta { get; set; }
    }

    public class ApccResponseMeta {

        [JsonProperty]
        public string String { get; set; }
    }
}
using System;
using System.Text.Json.Nodes;

namespace Smartalock.API
{
    public class SLResponse
    {
        public bool Result { get; }
        public int Code { get; }
        public string? Message { get; }
        public JsonNode? Info { get; }

        public SLResponse(bool result, int code, string? message, JsonNode? info)
        {
            this.Result = result;
            this.Code = code;
            this.Message = message;
            this.Info = info;
        }

        public static SLResponse Success(string message, JsonNode? info)
        {
            return new SLResponse(true, 0, message, info);
        }

        public static SLResponse Error(int code, string message, JsonNode? info)
        {
            return new SLResponse(false, code, message, info);
        }
    }
}


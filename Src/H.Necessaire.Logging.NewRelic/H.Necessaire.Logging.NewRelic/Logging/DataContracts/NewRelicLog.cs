using Newtonsoft.Json;

namespace H.Necessaire.Logging.NewRelic.Logging.DataContracts
{
    internal class NewRelicLog
    {
        [JsonProperty("timestamp", NullValueHandling = NullValueHandling.Ignore)]
        public long? TimestampInUnixMilliseconds { get; set; }


        [JsonProperty("attributes", NullValueHandling = NullValueHandling.Ignore)]
        public object Attributes { get; set; }

        [JsonProperty("message", Required = Required.Always)]
        public string Message { get; set; }
    }
}
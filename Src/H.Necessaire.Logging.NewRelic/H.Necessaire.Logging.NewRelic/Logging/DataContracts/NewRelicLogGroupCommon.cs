using Newtonsoft.Json;

namespace H.Necessaire.Logging.NewRelic.Logging.DataContracts
{
    internal class NewRelicLogGroupCommon
    {
        [JsonProperty("timestamp", NullValueHandling = NullValueHandling.Ignore)]
        public long? TimestampInUnixMilliseconds { get; set; }


        [JsonProperty("attributes", NullValueHandling = NullValueHandling.Ignore)]
        public object Attributes { get; set; }
    }
}
using Newtonsoft.Json;

namespace H.Necessaire.Logging.NewRelic.Logging.DataContracts
{
    internal class NewRelicLogGroup
    {
        [JsonProperty("common", NullValueHandling = NullValueHandling.Ignore)]
        public NewRelicLogGroupCommon Common { get; set; }


        [JsonProperty("logs", Required = Required.Always)]
        public NewRelicLog[] Logs { get; set; }
    }
}

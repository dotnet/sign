using Newtonsoft.Json;

namespace SignService.Models
{
    class ODataErrorValue
    {
        [JsonProperty(PropertyName = "item")]
        public string Item { get; set; }

        [JsonProperty(PropertyName = "value")]
        public object Value { get; set; }
    }
}

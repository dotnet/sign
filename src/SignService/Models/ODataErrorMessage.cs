using Newtonsoft.Json;

namespace SignService.Models
{
    class ODataErrorMessage
    {
        [JsonProperty(PropertyName = "value")]
        public string Value { get; set; }
    }
}

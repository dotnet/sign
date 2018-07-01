using Newtonsoft.Json;

namespace SignService.Models
{
    class ODataError
    {
        [JsonProperty(PropertyName = "code")]
        public string Code { get; set; }

        [JsonProperty(PropertyName = "message")]
        public ODataErrorMessage Message { get; set; }

        [JsonProperty(PropertyName = "values")]
        public ODataErrorValue[] Values { get; set; }
    }
}

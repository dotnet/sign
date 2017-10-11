using Newtonsoft.Json;

namespace SignService.Models
{
    class ODataErrorWrapper
    {
        [JsonProperty(PropertyName = "odata.error")]
        public ODataError ODataError { get; set; }
    }
}

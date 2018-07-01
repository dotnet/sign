using Newtonsoft.Json;

namespace SignService.Models
{
    class ODataScalar<T>
    {
        [JsonProperty(PropertyName = "value")]
        public T Value { get; set; }
    }
}

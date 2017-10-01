using System.Collections.Generic;
using Newtonsoft.Json;

namespace SignService.Models
{
    class ODataCollection<T>
    {
        [JsonProperty(PropertyName = "value")]
        public List<T> Value { get; set; }
    }
}

using Newtonsoft.Json;

namespace SignService.Models
{
    class ExtensionProperty
    {
        [JsonProperty(PropertyName = "objectId")]
        public string ObjectId { get; set; }

        [JsonProperty(PropertyName = "objectType")]
        public string ObjectType { get; set; }

        [JsonProperty(PropertyName = "appDisplayName")]
        public string AppDisplayName { get; set; }

        [JsonProperty(PropertyName = "dataType")]
        public string DataType { get; set; }

        [JsonProperty(PropertyName = "isMultiValued")]
        public bool? IsMultiValued { get; set; }

        [JsonProperty(PropertyName = "isSyncedFromOnPremises")]
        public bool? IsSyncedFromOnPremises { get; set; }

        [JsonProperty(PropertyName = "name")]
        public string Name { get; set; }

        [JsonProperty(PropertyName = "targetObjects")]
        public string[] TargetObjects { get; set; }
    }
}

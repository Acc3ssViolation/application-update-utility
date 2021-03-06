using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace ApplicationUpdateUtility.Shared
{
    public class JsonConverterVersion : JsonConverter<Version>
    {
        public override Version? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var str = reader.GetString();
            if (str is null)
                return null;
            return new Version(str);
        }

        public override void Write(Utf8JsonWriter writer, Version value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}

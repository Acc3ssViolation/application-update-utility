using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace ApplicationUpdateUtility.Shared
{
    public class JsonConverterByteArray : JsonConverter<byte[]>
    {
        public override byte[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var str = reader.GetString();
            return str?.ParseByteString();
        }

        public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToByteString());
        }
    }
}

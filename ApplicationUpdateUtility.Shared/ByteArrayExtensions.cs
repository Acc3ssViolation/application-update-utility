using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ApplicationUpdateUtility.Shared
{
    public static class ByteArrayExtensions
    {
        public static byte[] ParseByteString(this string value)
        {
            if (value is null)
                throw new ArgumentNullException(value);

            if (value.Length % 2 != 0)
                throw new ArgumentException($"Byte string {value} length is not a multiple of 2");

            var bytes = new byte[value.Length / 2];

            for (int i = 0; i < value.Length; i += 2)
            {
                var span = value.AsSpan(i, 2);
                if (!byte.TryParse(span, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var result))
                {
                    throw new ArgumentException($"Could not parse byte string {value}");
                }

                bytes[i / 2] = result;
            }

            return bytes;
        }

        public static string ToByteString(this byte[] value)
        {
            var builder = new StringBuilder();
            foreach (var b in value)
            {
                builder.Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }
    }
}

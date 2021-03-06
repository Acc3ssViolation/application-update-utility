using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace ApplicationUpdateUtility.Shared
{
    public class ManifestParseException : Exception
    {
        public ManifestParseException()
        {
        }

        public ManifestParseException(string? message) : base(message)
        {
        }

        public ManifestParseException(string? message, Exception? innerException) : base(message, innerException)
        {
        }

        protected ManifestParseException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}

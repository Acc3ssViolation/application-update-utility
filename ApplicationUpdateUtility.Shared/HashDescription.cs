using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ApplicationUpdateUtility.Shared
{
    public class HashDescription
    {
        public HashAlgorithm Algorithm { get; set; }
        public byte[] Hash { get; set; }

        public HashDescription()
        {
            Algorithm = HashAlgorithm.None;
            Hash = Array.Empty<byte>();
        }

        public HashDescription(HashAlgorithm algorithm, byte[] hash)
        {
            Algorithm = algorithm;
            Hash = hash ?? throw new ArgumentNullException(nameof(hash));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ApplicationUpdateUtility.Shared
{
    public class FileDescription
    {
        public string Path { get; set; }

        public ulong Size { get; set; }

        public UpdateMode UpdateMode { get; set; }

        public HashDescription Hash { get; set; }

        public FileDescription()
        {
            Path = string.Empty;
            UpdateMode = UpdateMode.NewOnly;
            Hash = new HashDescription();
            Size = 0;
        }

        public FileDescription(string path, ulong size, UpdateMode updateMode, HashDescription hash)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
            Size = size;
            UpdateMode = updateMode;
            Hash = hash ?? throw new ArgumentNullException(nameof(hash));
        }
    }
}

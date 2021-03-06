using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ApplicationUpdateUtility.Shared
{
    public class GroupDescription
    {
        public string Name { get; set; }

        public string Path { get; set; }

        public string Remote { get; set; }

        public IReadOnlyList<FileDescription> Files { get; set; }

        public GroupDescription()
        {
            Name = string.Empty;
            Path = string.Empty;
            Files = Array.Empty<FileDescription>();
            Remote = string.Empty;
        }

        public GroupDescription(string name, string path, string remote, IEnumerable<FileDescription> files)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Path = path ?? throw new ArgumentNullException(nameof(path));
            Remote = remote ?? throw new ArgumentNullException(nameof(remote));
            Files = files.ToArray() ?? throw new ArgumentNullException(nameof(files));
        }
    }
}

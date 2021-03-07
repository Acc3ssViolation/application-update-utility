using ApplicationUpdateUtility.Shared;
using System;
using System.Linq;

namespace ApplicationUpdateUtility
{
    partial class Program
    {
        public class FileDescriptionDiff
        {
            public FileDescription Old { get; }
            public FileDescription New { get; }

            public FileDescriptionDiff(FileDescription old, FileDescription @new)
            {
                Old = old ?? throw new ArgumentNullException(nameof(old));
                New = @new ?? throw new ArgumentNullException(nameof(@new));
            }

            public bool NeedsUpdate()
            {
                if (Old.Size != New.Size)
                    return true;

                if (Old.Hash.Algorithm != New.Hash.Algorithm)
                    return true;

                if (!Old.Hash.Hash.SequenceEqual(New.Hash.Hash))
                    return true;

                return false;
            }
        }
    }
}

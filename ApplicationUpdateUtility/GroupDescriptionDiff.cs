using ApplicationUpdateUtility.Shared;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ApplicationUpdateUtility
{
    partial class Program
    {
        public class GroupDescriptionDiff
        {
            public GroupDescription Old { get; }
            public GroupDescription New { get; }

            public IReadOnlyList<FileDescription> AddedFiles { get; }

            public IReadOnlyList<FileDescriptionDiff> UpdatedFiles { get; }

            public IReadOnlyList<FileDescription> RemovedFiles { get; }

            public GroupDescriptionDiff(GroupDescription old, GroupDescription @new)
            {
                Old = old ?? throw new ArgumentNullException(nameof(old));
                New = @new ?? throw new ArgumentNullException(nameof(@new));

                var updatedFiles = new List<FileDescriptionDiff>();

                AddedFiles = New.Files.Where(f => !Old.Files.Any(of => f.Path == of.Path)).ToList();
                RemovedFiles = Old.Files.Where(f => !New.Files.Any(of => f.Path == of.Path)).ToList();

                foreach (var updatedOldFile in Old.Files.Where(f => New.Files.Any(nf => f.Path == nf.Path)))
                {
                    var updatedNewFile = New.Files.First(nf => nf.Path == updatedOldFile.Path);

                    updatedFiles.Add(new FileDescriptionDiff(updatedOldFile, updatedNewFile));
                }

                UpdatedFiles = updatedFiles;
            }
        }
    }
}

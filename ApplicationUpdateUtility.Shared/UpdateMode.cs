using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ApplicationUpdateUtility.Shared
{
    /// <summary>
    /// Modes for updating files
    /// </summary>
    public enum UpdateMode
    {
        /// <summary>
        /// Existing files will be overwritten
        /// </summary>
        Overwrite,
        /// <summary>
        /// Updates will be appended to the end of an existing file
        /// </summary>
        Append,
        /// <summary>
        /// File will only be updated if it doesn't exist yet
        /// </summary>
        NewOnly,
    }
}

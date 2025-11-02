using System;

namespace QutieDTO.Models
{
    /// <summary>
    /// Represents an auto-assigned role that is given to users when they join the server
    /// </summary>
    public class AutoRole
    {
        /// <summary>
        /// Unique identifier for the auto role configuration
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Discord role ID to be automatically assigned
        /// </summary>
        public long RoleId { get; set; }

        /// <summary>
        /// Name of the role (for display purposes)
        /// </summary>
        public string RoleName { get; set; }

        /// <summary>
        /// When this auto-role was created
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Navigation property to the Role
        /// </summary>
        public virtual Role Role { get; set; }
    }
}

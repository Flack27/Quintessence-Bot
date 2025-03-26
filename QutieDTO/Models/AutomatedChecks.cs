using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QutieDTO.Models
{
    public partial class AutomatedChecks
    {
        public int Id { get; set; }

        public int CheckDelayMinutes { get; set; }
        public bool AutoRemoveAbsentUsers { get; set; }
        public bool AutoRemoveLateUsers { get; set; }
        public bool PingUsers { get; set; }
    }
}

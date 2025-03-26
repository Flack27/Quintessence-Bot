using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QutieDTO.Models
{
    public class UserMessageActivitySummary
    {
        public int Id { get; set; }
        public long UserId { get; set; }
        public DateTime Date { get; set; } // Just store the day (with time at 00:00:00)
        public int MessageCount { get; set; }
        public int XpEarned { get; set; }

        // Foreign key relationship
        public virtual User User { get; set; } = null!;
    }
}

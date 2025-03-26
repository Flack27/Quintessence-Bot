using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QutieDTO.Models
{
    public partial class ReactionRoles
    {
        public long Id { get; set; }
        public long ChannelId { get; set; }
        public long MessageId { get; set; }
        public string EmojiName { get; set; }
        public long EmojiId { get; set; } // 0 for Unicode emoji
        public long RoleId { get; set; }
    }
}

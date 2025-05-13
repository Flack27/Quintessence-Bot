using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QutieDTO.Models
{
    public partial class FormSubmission
    {
        public long SubmissionId { get; set; }

        public long UserId { get; set; }

        public long FormId { get; set; }

        public DateTime SubmitDate { get; set; }

        public bool? Approved { get; set; }
    }
}

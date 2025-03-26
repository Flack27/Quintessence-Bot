using QutieDTO.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QutieDTO
{
    public class GameFieldDefinition
    {
        public long Id { get; set; }
        public long GameId { get; set; }
        public string FieldName { get; set; }
        public string DisplayName { get; set; }
        public int DisplayOrder { get; set; }
        public bool IsRequired { get; set; }
        public string DataType { get; set; } = "String";

        // Navigation property
        public Game Game { get; set; }
    }
}
